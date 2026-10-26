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
// Schedules bounded periodic captures of dirty cartridge SRAM.
// Capture occurs on the hardware owner; durable writes run outside emulation.
//

namespace SNESEmulator.Runtime;

/// <summary>UI ticks schedule at most one off-thread save, without pausing the owner.</summary>
public sealed class BatteryAutosave(BatteryPersistence persistence,Action<string> error,double intervalSeconds=30)
{
    private double elapsed;
    public Task Pending {get;private set;}=Task.CompletedTask;
    public long Attempts {get;private set;}
    public void Tick(double seconds,bool enabled)
    {
        elapsed=Math.Min(intervalSeconds,elapsed+Math.Max(0,seconds));
        if(!enabled||elapsed<intervalSeconds||!Pending.IsCompleted)return;
        elapsed=0;Attempts++;
        Pending=Task.Run(async()=>{try{await persistence.FlushAsync().ConfigureAwait(false);}catch(Exception e){error("SRAM SAVE FAILED: "+e.Message);}});
    }
}
