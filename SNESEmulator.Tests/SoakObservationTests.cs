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
// Checks readiness and presentation quality counters without hiding cold or post-ready failures.
// Assertions establish their stated test scope, not general physical SNES certification.
//

using SNESEmulator;
using SNESEmulator.Frontend;
using SNESEmulator.Network;

internal static class SoakObservationTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static HostPresentation.Counters Counters(long muted, long frames = 100) => new(frames, muted, 1, 0);
    private static void Balanced(NetworkPlayout playout)
    {
        var counters = playout.ReadDropCounters();
        Check(counters.Total == playout.Dropped && counters.Total == counters.ClassifiedTotal, "Drop reasons differ from original cumulative total");
    }
    internal static void Run(Action<string, Action> test)
    {
        test("Soak screenshot failures retain counted errors and permit final reporting", () =>
        {
            int captures=0,errors=0;var messages=new List<string>();
            void Error(string message){errors++;messages.Add(message);}
            foreach(var size in new[]{(0,1),(1,0),(0,0),(-1,1)})
                Check(!SoakSmoke.TryCaptureScreenshot(size.Item1,size.Item2,()=>captures++,Error),"Missing drawable size cannot create a valid screenshot");
            Check(captures==0&&errors==4,"Invalid dimensions are counted without calling GDI/OpenGL");
            Check(SoakSmoke.TryCaptureScreenshot(256,224,()=>captures++,Error)&&captures==1&&errors==4,"Normal capture remains successful");
            Check(!SoakSmoke.TryCaptureScreenshot(256,224,()=>throw new IOException("capture fault"),Error)&&errors==5,"Actual capture exception remains a counted failure");
            Check(messages.Last().Contains("capture fault"),"Original failure remains visible");
        });
        test("Soak cumulative counter detects catchup between ten-second status samples", () =>
        {
            var observer = new SoakQualityTracker();
            observer.Observe(1, Counters(200), 5, 100, false, 0);
            observer.MarkReady(1.01, Counters(200), 5);
            observer.Observe(1.03, Counters(201, 102), 6, 102, false, 0);
            observer.Observe(10, Counters(201, 640), 6, 640, false, 0);
            Check(observer.SteadyActualCatchupFields == 1 && observer.SteadyRemoteDropped == 1, "Short actual counter increment was missed");
            Check(observer.Events.Single().SampledPacingLateSeconds == 0 && !observer.Events.Single().LatestCatchingUp, "Test must not rely on a still-visible late field");
        });
        test("Soak final cutoff observes catchup during the closing screenshot window", () =>
        {
            var observer = new SoakQualityTracker(); observer.MarkReady(5, Counters(200), 39);
            observer.Observe(3599.99, Counters(200, 216000), 39, 216000, false, 0);
            // Simulate work between the last render check and measurement cutoff.
            observer.Observe(3600.01, Counters(201, 216001), 40, 216001, false, 0);
            Check(observer.SteadyActualCatchupFields == 1 && observer.Events.Single().EntireWindowAfterReady, "Final-window increment was excluded");
        });
        test("Soak readiness freezes original cumulative baseline without resetting cold counts", () =>
        {
            var observer = new SoakQualityTracker();
            observer.MarkReady(5, Counters(234), 39);
            observer.Observe(5.01, Counters(234), 39, 300, false, 0);
            observer.Observe(90, Counters(253, 5400), 50, 5400, false, 0);
            Check(observer.ReadyCounters!.Value.MutedCatchupFields == 234 && observer.LastCounters.MutedCatchupFields == 253 && observer.SteadyActualCatchupFields == 19, "Cold baseline shifted");
            bool rejected = false; try { observer.MarkReady(91, Counters(253), 50); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "A second ready boundary was accepted");
        });
        test("Soak bounded detail overflow preserves full cumulative evidence", () =>
        {
            var observer = new SoakQualityTracker(); observer.MarkReady(0, Counters(0, 0), 0); observer.Observe(0, Counters(0, 0), 0, 0, false, 0);
            for (int i = 1; i <= SoakQualityTracker.EventCapacity + 36; i++) observer.Observe(i / 60d, Counters(i, i), i, i, true, .12);
            Check(observer.Events.Count == SoakQualityTracker.EventCapacity && observer.OmittedChangeEvents == 36 && observer.SteadyActualCatchupFields == SoakQualityTracker.EventCapacity + 36, "Bounded event detail lost cumulative increments");
            Check(observer.Events.First().LatestField == 37 && observer.Events.Last().LatestField == SoakQualityTracker.EventCapacity + 36, "Cold detail displaced the latest steady context");
        });
        test("Soak counter regression remains visible without clamping", () =>
        {
            var observer = new SoakQualityTracker(); observer.MarkReady(0, Counters(10), 5); observer.Observe(0, Counters(10), 5, 1, false, 0); observer.Observe(1, Counters(9), 4, 2, false, 0);
            Check(observer.CounterRegressions == 1 && observer.SteadyActualCatchupFields == -1, "Counter reset was hidden");
        });
        test("Playout timeline capacity drift and shutdown drops are disjoint", () =>
        {
            double now = 0; var playout = new NetworkPlayout(() => { }, clock: () => now, automatic: false);
            playout.Enqueue(0, true, () => { }); playout.Reset();
            for (int i = 0; i <= NetworkPlayout.Capacity; i++) playout.Enqueue(0, true, () => { });
            now = 1; playout.Enqueue(0, true, () => { }); playout.Dispose();
            var c = playout.ReadDropCounters(); Balanced(playout);
            Check(c.TimelineReset == 1 && c.CapacityReset == NetworkPlayout.Capacity && c.SourceDriftReset == 1 && c.ShutdownReset == 1, "Reset cause classification differs");
        });
        test("Playout invalid generation keeps original rejection and classified count", () =>
        {
            double now = 0; using var playout = new NetworkPlayout(() => { }, clock: () => now, automatic: false);
            bool delivered = false; playout.Enqueue(0, true, () => delivered = true); now = .05; playout.BeforeCommit = () => playout.Reset();
            Check(playout.PumpOnce() && !delivered && playout.ReadDropCounters().InvalidGeneration == 1, "Invalid generation published"); Balanced(playout);
        });
        test("Playout missing audio clock and freshness rejection reasons preserve thresholds", () =>
        {
            double now = 0; using var missing = new NetworkPlayout(() => { }, clock: () => now, automatic: false, needsAudio: () => true);
            missing.Enqueue(0, false, () => throw new Exception("must reject")); now = .3; Check(missing.PumpOnce() && missing.ReadDropCounters().MissingAudioClock == 1, "Missing driver clock reason"); Balanced(missing);
            now = 0; using var late = new NetworkPlayout(() => { }, clock: () => now, automatic: false); late.Enqueue(0, true, () => throw new Exception("must reject")); now = .2; Check(late.PumpOnce() && late.ReadDropCounters().LateAudio == 1, "Late audio reason"); Balanced(late);
            now = 0; using var video = new NetworkPlayout(() => { }, () => .04, () => now, automatic: false); video.Enqueue(0, false, () => throw new Exception("must reject")); now = .04; Check(video.PumpOnce() && video.ReadDropCounters().DistantVideo == 1 && video.MaximumSoftwareAvErrorSeconds == 0, "40ms stale video boundary changed"); Balanced(video);
        });
        test("Playout callback failure counts rejected callback and queued reset separately", () =>
        {
            double now = 0; using var playout = new NetworkPlayout(() => { }, clock: () => now, automatic: false);
            playout.Enqueue(0, true, () => throw new IOException("intentional diagnostic mutation")); playout.Enqueue(.01, true, () => { }); now = .05;
            Check(playout.PumpOnce(), "Due callback not attempted"); var c = playout.ReadDropCounters(); Balanced(playout);
            Check(c.DeliveryFailure == 1 && c.FailureReset == 1 && c.Total == 2 && playout.Fault != null, "Callback/reset drops overlap");
        });
        test("Playout concurrent reset enqueue and shutdown snapshots remain balanced", () =>
        {
            var playout = new NetworkPlayout(() => { }, clock: () => 0, automatic: false);
            Task.WaitAll(Task.Run(() => { for (int i = 0; i < 1000; i++) playout.Enqueue(0, true, () => { }); }), Task.Run(() => { for (int i = 0; i < 1000; i++) playout.Reset(); }), Task.Run(() => { for (int i = 0; i < 1000; i++) { var c = playout.ReadDropCounters(); Check(c.Total == c.ClassifiedTotal, "Concurrent partial reason snapshot"); } }));
            playout.Dispose(); Balanced(playout); Check(playout.Dropped == 1000, "An enqueued item was counted twice or missed");
        });
    }
}
