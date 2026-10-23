using SNESEmulator.Core;
using System.Windows.Forms;
namespace SNESEmulator.Frontend;
internal static class BindingEditor
{
    internal static void Probe(string output)
    {
        using var timer=new System.Windows.Forms.Timer{Interval=100};int stage=0;Form? active=null;TextBox? input=null;Button? save=null;
        timer.Tick+=(_,_)=>{if(active==null||input==null||save==null)return;if(stage++==0){input.Text="Axis999+";save.PerformClick();if(active.DialogResult==DialogResult.OK)throw new InvalidOperationException("Invalid binding accepted");return;}timer.Stop();input.Text="Axis2+&Button4";using(var image=new System.Drawing.Bitmap(active.Width,active.Height)){active.DrawToBitmap(image,new System.Drawing.Rectangle(0,0,image.Width,image.Height));image.Save(Path.Combine(output,"manual-binding-editor.png"));}save.PerformClick();};
        string? result=Edit("Turbo","",false,true,(form,box,button)=>{active=form;input=box;save=button;timer.Start();});
        if(result!="Axis2+&Button4")throw new InvalidOperationException("Native manual binding editor failed");
        File.WriteAllText(Path.Combine(output,"manual-editor-result.json"),System.Text.Json.JsonSerializer.Serialize(new{Success=true,InvalidRejected=true,Binding=result,SyntheticAppHooks=true}));
    }
    internal static string? Edit(string name,string current,bool keyboard,bool mappedReferences,Action<Form,TextBox,Button>? shown=null)
    {
        using var dialog=new Form{Text="SNES binding: "+name,Width=600,Height=260,StartPosition=FormStartPosition.CenterScreen,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false};
        var help=new Label{UseMnemonic=false,Left=16,Top=16,Width=550,Height=65,Text="; = alternatives, & = simultaneous chord. Empty = unbound.\n"+(keyboard?"Keys: Up, Enter, ShiftRight, F1..F12, A..Z, Number0..9 ...":"Inputs: Button0..255, Axis0+ / Axis0-, Hat0Up; actions also @Start ...")+"\nFrontend actions take priority; release all held inputs before playing."};
        var input=new TextBox{Left=16,Top=90,Width=550,Text=current,MaxLength=128};
        var error=new Label{Left=16,Top=125,Width=550,Height=35,ForeColor=System.Drawing.Color.Firebrick};
        input.TextChanged+=(_,_)=>error.Text="";
        var save=new Button{Left=380,Top=168,Width=90,Text="Save"};var cancel=new Button{Left=480,Top=168,Width=90,Text="Cancel",DialogResult=DialogResult.Cancel};
        save.Click+=(_,_)=>{if(!InputBindings.Valid(input.Text,keyboard,mappedReferences)){error.Text="Invalid input. At most 4 alternatives, each with 1-4 chord members.";return;}dialog.DialogResult=DialogResult.OK;dialog.Close();};
        dialog.Controls.AddRange([help,input,error,save,cancel]);dialog.AcceptButton=save;dialog.CancelButton=cancel;if(shown!=null)dialog.Shown+=(_,_)=>shown(dialog,input,save);
        return dialog.ShowDialog()==DialogResult.OK?input.Text:null;
    }
}
