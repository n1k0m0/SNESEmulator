// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy at https://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See LICENSE and NOTICE for permissions and attribution.
//
// Maintains the remote software video/audio presentation timeline.
// Buffer recovery and presentation clock selection are independent of core execution.
//

namespace SNESEmulator.Runtime;

/// <summary>Transport status and encoding are independently fallible presentation sinks.</summary>
public sealed class IsolatedNetworkPresentation(Action<WorkerStatus> status,Action<FieldOutput,Func<WorkerStatus>> output,Action<string> error)
{
    public long Failures {get;private set;}
    public string? LastError {get;private set;}
    public void Update(WorkerStatus value)=>Run(()=>status(value));
    public void Present(FieldOutput field,Func<WorkerStatus> read)=>Run(()=>output(field,read));
    private void Run(Action sink)
    {
        try{sink();}
        catch(Exception e){Failures++;LastError=e.ToString();error("NETWORK PRESENTATION FAILED: "+e.Message);}
    }
}
