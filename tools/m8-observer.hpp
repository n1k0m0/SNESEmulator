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
// Captures timed hardware observations from the external laboratory reference process.
//

// Observer declaration injected only into isolated external laboratory builds.
// No clock, bus, register or audio value is modified by the observer.
void m8_capture(int left, int right);
void m8_bus(unsigned address,unsigned value,bool write,unsigned v,unsigned h);
void m8_tick(bool field);
void m8_clock(bool field,unsigned v,unsigned h);
