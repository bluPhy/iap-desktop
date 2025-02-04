﻿//
// Copyright 2022 Google LLC
//
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
//

using Google.Solutions.Apis.Locator;
using Google.Solutions.Common.Diagnostics;
using Google.Solutions.IapDesktop.Application.Services.Adapters;
using Google.Solutions.IapDesktop.Application.Services.Integration;
using System.Threading;
using System.Threading.Tasks;

namespace Google.Solutions.IapDesktop.Application.Services.Management
{
    public interface IInstanceControlService
    {
        /// <summary>
        /// Start, stop, or otherwise control the lifecycle of an instance
        /// and notify other services.
        /// </summary>
        Task ControlInstanceAsync(
            InstanceLocator instance,
            InstanceControlCommand command,
            CancellationToken cancellationToken);
    }

    public sealed class InstanceControlService : IInstanceControlService
    {
        private readonly IComputeEngineAdapter computeEngineAdapter;
        private readonly IEventService eventService;

        public InstanceControlService(
            IComputeEngineAdapter computeEngineAdapter,
            IEventService eventService)
        {
            this.computeEngineAdapter = computeEngineAdapter;
            this.eventService = eventService;
        }

        //---------------------------------------------------------------------
        // InstanceControlService.
        //---------------------------------------------------------------------

        public async Task ControlInstanceAsync(
            InstanceLocator instance,
            InstanceControlCommand command,
            CancellationToken cancellationToken)
        {
            using (ApplicationTraceSources.Default.TraceMethod()
                .WithParameters(instance, command))
            {
                await this.computeEngineAdapter.ControlInstanceAsync(
                    instance,
                    command,
                    cancellationToken)
                .ConfigureAwait(false);

                await this.eventService.FireAsync(
                    new InstanceStateChangedEvent(
                        instance,
                        command == InstanceControlCommand.Start ||
                            command == InstanceControlCommand.Resume))
                    .ConfigureAwait(false);
            }
        }
    }


    public class InstanceStateChangedEvent
    {
        public InstanceLocator Instance { get; }

        public bool IsRunning { get; }

        public InstanceStateChangedEvent(
            InstanceLocator instance,
            bool isRunning)
        {
            this.Instance = instance;
            this.IsRunning = isRunning;
        }
    }
}
