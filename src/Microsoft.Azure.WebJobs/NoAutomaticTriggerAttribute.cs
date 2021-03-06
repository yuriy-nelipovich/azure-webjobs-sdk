﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Attribute used to mark a job function that will not be automatically triggered.
    /// </summary>
    /// <remarks>
    /// This attribute is useful in two cases:
    /// <list type="number">
    /// <item>
    /// <term>Functions with triggers</term>
    /// <description>Prevents automatic invocation of the triggers, allowing manual polling.</description>
    /// </item>
    /// <item>
    /// <term>Functions without other attributes</term>
    /// <description>Flags the function as an available job function.</description>
    /// </item>
    /// </list>
    /// In both cases, functions marked with this attribute are never called automatically by JobHost (during
    /// RunAndBlock). Instead, they must be invoked manually using the Call method.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class NoAutomaticTriggerAttribute : Attribute
    {
    }
}
