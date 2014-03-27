﻿using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage.Table.DataServices;

namespace Microsoft.WindowsAzure.Jobs.Host.Storage.Table
{
    internal interface ICloudTable
    {
        T GetOrInsert<T>(T entity) where T : TableServiceEntity;
        void InsertEntity<T>(T entity) where T : TableServiceEntity;
        IEnumerable<T> Query<T>(int limit, params IQueryModifier[] queryModifiers) where T : TableServiceEntity;
    }
}
