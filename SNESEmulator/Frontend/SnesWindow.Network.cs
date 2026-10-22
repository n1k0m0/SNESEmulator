using OpenTK.Input;
using SNESEmulator.Network;

namespace SNESEmulator.Frontend;

public sealed partial class SnesWindow
{
    private SnesNetTlsPinChange? pinChange;
    private bool retryHost;
    private string[] NetworkRows()
    {
        var clients=server.GetClientSnapshots();
        if(clients.Count>0) selectedClient=Math.Clamp(selectedClient,0,clients.Count-1);
        var chosen=clients.Count>0?clients[selectedClient]:null;
        return [
            "MODE             "+(settings.Relay?"RELAY":"LAN TLS"),
            "SERVER PORT      "+settings.Port,
            "SERVER PASSWORD  "+(password.Length==0?"NONE":new string('*',password.Length)),
            "SERVER           "+(server.IsRunning?"STOP SERVER":"START SERVER"),
            "SELECT CLIENT    "+(chosen==null?"NONE":chosen.ClientId+" "+chosen.Name),
            "CONTROLLER RIGHT "+(chosen==null?"NONE":chosen.Permission.ToString()),
            "KICK CLIENT",
            "PLAYER NAME      "+settings.PlayerName,
            "CLIENT HOST      "+settings.Host,
            "CLIENT ROLE      "+(settings.Observer?"OBSERVER":"PLAYER"),
            "CLIENT           "+(client.IsConnected?"CLIENT LEAVE":"CLIENT JOIN"),
            "RELAY HOST       "+settings.RelayHost,
            "RELAY PORT       "+settings.RelayPort,
            "CONNECTION ID    "+settings.ConnectionId,
            "RELAY PASSWORD   "+(relayPassword.Length==0?"NONE":new string('*',relayPassword.Length)),
            "CERTIFICATE      "+(pinChange==null?client.ServerCertificateFingerprint:"CHANGED - ENTER TO REVIEW")
        ];
    }
    private void ActivateNetwork(int direction)
    {
        if(busy) { status="NETWORK CONNECTION IN PROGRESS"; return; }
        var clients=server.GetClientSnapshots();
        var chosen=clients.Count>0?clients[Math.Clamp(selectedClient,0,clients.Count-1)]:null;
        switch(selection)
        {
            case 0: if(!server.IsRunning && !client.IsConnected) settings.Relay=!settings.Relay; break;
            case 1: settings.Port=Math.Clamp(settings.Port+direction,1,65535); break;
            case 3:
                if(server.IsRunning) server.Stop();
                else if(!client.IsConnected) BeginConnection(true);
                break;
            case 4: if(clients.Count>0) selectedClient=(selectedClient+direction+clients.Count)%clients.Count; break;
            case 5: if(chosen!=null) server.CycleClientPermission(chosen.ClientId); break;
            case 6: if(chosen!=null) server.KickClient(chosen.ClientId); break;
            case 9: if(!client.IsConnected) settings.Observer=!settings.Observer; break;
            case 10:
                if(client.IsConnected) { client.Disconnect(); lock(client) remotePixels=null; hostMenu=""; }
                else if(!server.IsRunning) BeginConnection(false);
                break;
            case 12: settings.RelayPort=Math.Clamp(settings.RelayPort+direction,1,65535); break;
            case 15:
                if(pinChange!=null)
                {
                    var p=pinChange;
                    var answer=System.Windows.Forms.MessageBox.Show(
                        $"Certificate for {p.Host}:{p.Port} changed.\n\nOld SHA-256:\n{p.OldFingerprint}\n\nNew SHA-256:\n{p.NewFingerprint}\n\nTrust the new certificate and reconnect?",
                        "SNESEmulator - TLS certificate changed",System.Windows.Forms.MessageBoxButtons.YesNo,System.Windows.Forms.MessageBoxIcon.Warning);
                    if(answer==System.Windows.Forms.DialogResult.Yes)
                    { SnesNetTls.ReplaceTrustedServerCertificate(p.Host,p.Port,p.NewFingerprint); pinChange=null; BeginConnection(retryHost); }
                }
                break;
        }
        settings.Save();
    }
    private bool EditNetwork(Key key,KeyModifiers modifiers)
    {
        if(busy || selection is not (1 or 2 or 7 or 8 or 11 or 12 or 13 or 14)) return false;
        bool numeric=selection is 1 or 12;
        string value=selection switch
        {
            1=>settings.Port.ToString(),2=>password,7=>settings.PlayerName,8=>settings.Host,
            11=>settings.RelayHost,12=>settings.RelayPort.ToString(),13=>settings.ConnectionId,_=>relayPassword
        };
        if(key==Key.Delete) value="";
        else if(key==Key.BackSpace) value=value.Length==0?"":value[..^1];
        else
        {
            char ch=key switch
            {
                >=Key.A and <=Key.Z => (char)(((modifiers&KeyModifiers.Shift)!=0?'A':'a')+key-Key.A),
                >=Key.Number0 and <=Key.Number9 => (char)('0'+key-Key.Number0),
                Key.Period=>'.',Key.Minus=>'-',Key.Space=>' ',_=>'\0'
            };
            if(ch=='\0' || numeric && !char.IsAsciiDigit(ch)) return false;
            value=Clip(value+ch,128);
        }
        switch(selection)
        {
            case 1: if(int.TryParse(value,out int port)) settings.Port=Math.Clamp(port,1,65535); break;
            case 2: password=value; break;
            case 7: settings.PlayerName=value; break;
            case 8: settings.Host=value; break;
            case 11: settings.RelayHost=value; break;
            case 12: if(int.TryParse(value,out int rp)) settings.RelayPort=Math.Clamp(rp,1,65535); break;
            case 13: settings.ConnectionId=value; break;
            case 14: relayPassword=value; break;
        }
        settings.Save(); return true;
    }
    private void BeginConnection(bool host)
    {
        busy=true; status="CONNECTING...";
        // Capture UI settings; network I/O must never block the OpenTK thread.
        bool relay=settings.Relay,observer=settings.Observer;
        string address=settings.Host,relayAddress=settings.RelayHost,id=settings.ConnectionId,name=settings.PlayerName;
        string secret=password,relaySecret=relayPassword;
        int port=settings.Port,relayPort=settings.RelayPort;
        _=Task.Run(()=>
        {
            try
            {
                string result="SERVER STARTED";
                if(host)
                { if(relay) server.StartRelay(relayAddress,relayPort,id,relaySecret,secret); else server.Start(port,secret); }
                else
                {
                    var role=observer?SnesNetClientRole.Observer:SnesNetClientRole.Player;
                    if(relay) client.ConnectRelay(relayAddress,relayPort,id,relaySecret,secret,role,name,out result);
                    else client.Connect(address,port,secret,role,name,out result);
                }
                pending.Enqueue(()=>{ status=result; busy=false; });
            }
            catch(SnesNetTlsException e) when(e.PinChange!=null)
            { pending.Enqueue(()=>{ pinChange=e.PinChange; retryHost=host; status="TLS CERTIFICATE CHANGED - REVIEW CERTIFICATE ROW"; busy=false; }); }
            catch(Exception e) { pending.Enqueue(()=>{ status=e.Message; busy=false; }); }
        });
    }
}
