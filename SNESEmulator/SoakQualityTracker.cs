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
// Classifies post-readiness presentation quality using explicit counters.
// Cold readiness, muted fields and remote drops remain separate observations.
//

using SNESEmulator.Frontend;

namespace SNESEmulator;

/// <summary>
/// Observes cumulative presentation counters at every native render callback.
/// A short catchup remains visible after PacingLateSeconds has returned to zero.
/// Detail rows are bounded; cumulative totals continue after detail overflow.
/// </summary>
internal sealed class SoakQualityTracker
{
    internal const int EventCapacity = 256;
    internal sealed record Change(double WindowStartSeconds, double ObservedSeconds,
        HostPresentation.Counters Previous, HostPresentation.Counters Current,
        long PreviousRemoteDropped, long RemoteDropped, long LatestField,
        bool LatestCatchingUp, double SampledPacingLateSeconds, bool EntireWindowAfterReady);

    private readonly List<Change> events = new();
    private HostPresentation.Counters previous;
    private long previousRemote;
    private double previousAt;
    private bool initialized;
    private HostPresentation.Counters? readyCounters;
    private long readyRemote;
    internal double? ReadySeconds { get; private set; }
    internal HostPresentation.Counters LastCounters { get; private set; }
    internal long LastRemoteDropped { get; private set; }
    internal long CounterRegressions { get; private set; }
    internal long OmittedChangeEvents { get; private set; }
    internal long SteadyActualCatchupFields => readyCounters is { } baseline ? LastCounters.MutedCatchupFields - baseline.MutedCatchupFields : 0;
    internal long SteadyRemoteDropped => readyCounters.HasValue ? LastRemoteDropped - readyRemote : 0;
    internal IReadOnlyList<Change> Events => events;
    internal HostPresentation.Counters? ReadyCounters => readyCounters;

    internal void MarkReady(double seconds, HostPresentation.Counters counters, long remoteDropped)
    {
        if (ReadySeconds.HasValue) throw new InvalidOperationException("Readiness is immutable");
        ReadySeconds = seconds;
        readyCounters = counters;
        readyRemote = remoteDropped;
        LastCounters = counters;
        LastRemoteDropped = remoteDropped;
    }

    internal void Observe(double seconds, HostPresentation.Counters counters, long remoteDropped,
        long latestField, bool latestCatchingUp, double sampledPacingLateSeconds)
    {
        if (initialized)
        {
            if (counters.Frames < previous.Frames || counters.MutedCatchupFields < previous.MutedCatchupFields ||
                counters.Discontinuities < previous.Discontinuities || counters.DeviceOverflows < previous.DeviceOverflows || remoteDropped < previousRemote)
                CounterRegressions++;
            if (counters.MutedCatchupFields != previous.MutedCatchupFields || counters.Discontinuities != previous.Discontinuities ||
                counters.DeviceOverflows != previous.DeviceOverflows || remoteDropped != previousRemote)
            {
                var change = new Change(previousAt, seconds, previous, counters, previousRemote, remoteDropped,
                    latestField, latestCatchingUp, sampledPacingLateSeconds, ReadySeconds is { } ready && previousAt >= ready);
                // Retain the latest bounded context. A long cold start must not
                // consume all detail slots before the first steady-state episode.
                if (events.Count == EventCapacity) { events.RemoveAt(0); OmittedChangeEvents++; }
                events.Add(change);
            }
        }
        initialized = true;
        previous = counters;
        previousRemote = remoteDropped;
        previousAt = seconds;
        LastCounters = counters;
        LastRemoteDropped = remoteDropped;
    }
}
