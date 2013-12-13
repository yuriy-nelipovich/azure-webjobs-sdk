﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AzureTables;
using DaasEndpoints;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Orchestrator;
using RunnerInterfaces;
using SimpleBatch;

namespace IndexDriver
{
    // $$$ Use this
    internal class IndexDriverInput
    {
        // Describes the cloud resource for what to be indexed.
        // This includes a blob download (or upload!) location
        public IndexRequestPayload Request { get; set; }

        // This can be used to download assemblies locally for inspection.
        public string LocalCache { get; set; }
    }

    internal class IndexResults
    {
        public IndexResults()
        {
        }

        public FunctionDefinition[] NewFunctions { get; set; }

        public FunctionDefinition[] UpdatedFunctions { get; set; }

        public FunctionDefinition[] DeletedFunctions { get; set; }

        public string[] Errors { get; set; }
    }

    // Do indexing in app.
    internal class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length == 1)
            {
                MainLocal(args);
            }

            var client = new Utility.ProcessExecuteArgs<IndexDriverInput, IndexResults>(args);

            IndexDriverInput descr = client.Input;
            var result = MainWorker(descr);

            client.Result = result;
        }

        // Index an assembly on the local disk. 
        private static void MainLocal(string[] args)
        {
            string assemblyPath = args[0];

            var table = AzureTable<FunctionDefinition>.NewInMemory();
            IFunctionTable settings = new LoggingCloudIndexerSettings(table);
            Indexer i = new Indexer(settings);

            var assembly = Assembly.LoadFile(assemblyPath);
            //string dir = Path.GetDirectoryName(assemblyPath);
            i.IndexAssembly(m => new MethodInfoFunctionLocation(m), assembly);

            // Print output
            foreach (var func in settings.ReadAll())
            {
                Console.WriteLine("  {0}", func.Location.GetShortName());
            }
        }

        public static IndexResults MainWorker(IndexDriverInput input)
        {
            string urlLogger = null;
            StringWriter buffer = null;

            try
            {
                string localCache = input.LocalCache;
                IndexRequestPayload payload = input.Request;

                if (payload.Writeback != null)
                {
                    buffer = new StringWriter();
                    Console.SetOut(buffer);
                }

                IAccountInfo accountInfo = new AccountInfo { AccountConnectionString = payload.ServiceAccountConnectionString };
                var services = new Services(accountInfo);

                IAzureTable<FunctionDefinition> table = new AzureTable<FunctionDefinition>(services.Account, EndpointNames.FunctionIndexTableName);
                LoggingCloudIndexerSettings settings = new LoggingCloudIndexerSettings(table);

                // ### This can go away now that we have structured return results
                urlLogger = payload.Writeback;

                IndexOperation indexOperation = payload.Operation as IndexOperation;
                if (indexOperation != null)
                {
                    var binderLookupTable = services.GetBinderTable();

                    HashSet<string> funcsBefore = settings.GetFuncSet();

                    Indexer i = new Indexer(settings);

                    var path = new CloudBlobPath(indexOperation.Blobpath);

                    var cd = new CloudBlobDescriptor
                    {
                        AccountConnectionString = indexOperation.UserAccountConnectionString,
                        ContainerName = path.ContainerName,
                        BlobName = path.BlobName
                    };

                    if (urlLogger != null)
                    {
                        Console.WriteLine("Logging output to: {0}", urlLogger);
                        CloudBlob blob = new CloudBlob(urlLogger);
                        blob.UploadText(string.Format("Beginning indexing {0}", indexOperation.Blobpath));
                    }

                    Console.WriteLine("indexing: {0}", indexOperation.Blobpath);

                    i.IndexContainer(cd, localCache, binderLookupTable);

                    // Log what changes happned (added, removed, updated)
                    // Compare before and after
                    HashSet<string> funcsAfter = settings.GetFuncSet();

                    var funcsTouched = from func in settings._funcsTouched select func.ToString();
                    PrintDifferences(funcsBefore, funcsAfter, funcsTouched);

                    Console.WriteLine("DONE: SUCCESS");
                }

                DeleteOperation deleteOperation = payload.Operation as DeleteOperation;
                if (deleteOperation != null)
                {
                    var function = services.GetFunctionTable().Lookup(deleteOperation.FunctionToDelete);
                    if (function == null)
                    {
                        Console.WriteLine("ERROR: The function '{0}' was not found.", deleteOperation.FunctionToDelete);
                    }
                    else
                    {
                        settings.Delete(function);
                        Console.WriteLine("DONE: Function '{0}' Deleted.", function.Location.GetShortName());
                    }
                }
            }
            catch (IndexException e)
            {
                // Expected user error
                Console.WriteLine("Error during indexing: {0}", e);
                Console.WriteLine("DONE: FAILED");
            }
            catch (Exception e)
            {
                // For other errors, be specific
                Console.WriteLine("Error during indexing: {0}", e);
                Console.WriteLine(e.GetType().FullName);
                Console.WriteLine(e.StackTrace);
                Console.WriteLine("DONE: FAILED");
            }

            if ((urlLogger != null) && (buffer != null))
            {
                CloudBlob blob = new CloudBlob(urlLogger);
                blob.UploadText(buffer.ToString());
            }

            return new IndexResults();
        }

        private class LoggingCloudIndexerSettings : FunctionTable
        {
            public List<FunctionDefinition> _funcsTouched = new List<FunctionDefinition>();

            public List<Type> BinderTypes = new List<Type>();

            public LoggingCloudIndexerSettings(IAzureTable<FunctionDefinition> table)
                : base(table)
            {
            }

            public override void Add(FunctionDefinition func)
            {
                _funcsTouched.Add(func);
                base.Add(func);
            }

            public HashSet<string> GetFuncSet()
            {
                HashSet<string> funcsAfter = new HashSet<string>();
                funcsAfter.UnionWith(from func in this.ReadAll() select func.ToString());
                return funcsAfter;
            }
        }

        private static void PrintDifferences(IEnumerable<string> funcsBefore, IEnumerable<string> funcsAfter, IEnumerable<string> funcsTouched)
        {
            // Removed. In before, not in after
            var setRemoved = new HashSet<string>(funcsBefore);
            setRemoved.ExceptWith(funcsAfter);

            if (setRemoved.Count > 0)
            {
                Console.WriteLine("Functions removed:");
                foreach (var func in setRemoved)
                {
                    Console.WriteLine("  {0}", func);
                }
                Console.WriteLine();
            }

            // Added
            var setAdded = new HashSet<string>(funcsAfter);
            setAdded.ExceptWith(funcsBefore);

            if (setAdded.Count > 0)
            {
                Console.WriteLine("Functions added:");
                foreach (var func in setAdded)
                {
                    Console.WriteLine("  {0}", func);
                }
                Console.WriteLine();
            }

            // Touched (not including added)
            HashSet<string> setTouched = new HashSet<string>(funcsTouched);
            setTouched.ExceptWith(setAdded);
            if (setTouched.Count > 0)
            {
                Console.WriteLine("Functions updated:");
                foreach (var func in setTouched)
                {
                    Console.WriteLine("  {0}", func);
                }
                Console.WriteLine();
            }
        }
    }
}