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
// Publishes host input snapshots for deterministic field-bound consumption.
// Menu handling and worker ownership prevent direct UI-thread hardware mutation.
//

using SNESEmulator.Core;
namespace SNESEmulator.Runtime;

public sealed record FrontendInputDecision(ushort GameButtons,ushort Pressed,IReadOnlySet<string> Actions,bool Active);
/// <summary>One source's focus, connection and configuration lease. A changed lease requires a neutral sample.</summary>
public sealed class FrontendInputSession
{
    private string? identity;
    private bool armed,suppressed=true;
    private ushort previous;
    private HashSet<string> previousActions=[];
    public void Reset(){identity=null;armed=false;suppressed=true;previous=0;previousActions.Clear();}
    public FrontendInputDecision Update(bool focused,string? source,IReadOnlySet<string> raw,IReadOnlyDictionary<string,string> buttons,IReadOnlyDictionary<string,string> actions,bool menu,bool capture)
    {
        FrontendInputDecision Neutral()=>new(0,0,new HashSet<string>(),false);
        if(!focused||source==null){Reset();return Neutral();}
        if(identity!=source){Reset();identity=source;}
        if(!armed){if(raw.Count==0){armed=true;suppressed=false;}return Neutral();}
        ushort mapped=0;var matchRaw=raw.ToHashSet();
        foreach(string name in InputBindings.Buttons)if(buttons.TryGetValue(name,out string? expression)&&InputBindings.Matches(expression,raw)&&Enum.TryParse<SnesButtons>(name,out var button)){mapped|=(ushort)button;matchRaw.Add("@"+name);}
        var rawActions=actions.Where(a=>InputBindings.Matches(a.Value,matchRaw)).Select(a=>a.Key).ToHashSet();
        var activeActions=rawActions.Where(a=>menu||a is not ("MenuSelect" or "MenuBack")).ToHashSet();
        var edges=activeActions.Except(previousActions).ToHashSet();previousActions=rawActions;
        ushort pressed=(ushort)(mapped&~previous);previous=mapped;
        if(menu||capture||activeActions.Overlaps(["MainMenu","SaveStates","Diagnostic"]))suppressed=true;
        if(raw.Count==0)suppressed=false;
        var consumed=activeActions.SelectMany(name=>InputBindings.Consumed(actions[name],matchRaw)).ToHashSet();
        foreach(string reference in consumed.Where(t=>t.StartsWith("@")).ToArray())
            if(buttons.TryGetValue(reference[1..],out string? expression))consumed.UnionWith(InputBindings.Consumed(expression,raw));
        var remaining=raw.Except(consumed).ToHashSet();ushort game=0;
        foreach(string name in InputBindings.Buttons)if(!consumed.Contains("@"+name)&&buttons.TryGetValue(name,out string? expression)&&InputBindings.Matches(expression,remaining)&&Enum.TryParse<SnesButtons>(name,out var button))game|=(ushort)button;
        if(capture){edges.Clear();previousActions=actions.Keys.ToHashSet();}
        return new(suppressed||menu||capture?(ushort)0:game,capture?(ushort)0:pressed,edges,!capture);
    }
}
