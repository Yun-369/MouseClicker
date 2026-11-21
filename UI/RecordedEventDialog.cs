using System.ComponentModel;
using MouseClicker.Domain;
namespace MouseClicker.UI;

// Event editor dialog. Only the fields that MEAN something for the selected event
// type are shown and submitted, so a mouse event can never carry leftover keyboard
// parameters (and vice versa) after the user switches the type in the dialog.
public sealed class RecordedEventDialog:Form
{
    private readonly EventValues values;
    private readonly PropertyGrid grid;
    public RecordedEvent Result=>values.ToEvent();
    public RecordedEventDialog(RecordedEvent value)
    {
        values=new EventValues(value);Text="编辑事件（类型、按键与坐标）";Size=new Size(520,430);StartPosition=FormStartPosition.CenterParent;
        grid=new PropertyGrid(){Dock=DockStyle.Fill,ToolbarVisible=false,HelpVisible=false};
        grid.SelectedObject=new EventView(values);
        grid.PropertyValueChanged+=(_,e)=>{
            if(e.ChangedItem?.PropertyDescriptor?.Name!="Type")return;
            values.NormalizeForType(values.Type);   // reset fields the new type does not use
            grid.SelectedObject=null;
            grid.SelectedObject=new EventView(values);
        };
        var ok=new Button(){Text="应用",Dock=DockStyle.Bottom,Height=36};
        ok.Click+=(_,_)=>{ok.Focus();if(!ValidateChildren())return;try{Storage.RecordingStore.Validate(new RecordingDocument(){Events=[Result with{TimeMs=Result.DelayMs}]});DialogResult=DialogResult.OK;}catch(Exception ex){MessageBox.Show(this,ex.Message);}};
        Controls.Add(grid);Controls.Add(ok);AcceptButton=ok;
    }

    // Filtered descriptor list per event type; switching the type rebinds the grid.
    private sealed class EventView(EventValues v):CustomTypeDescriptor
    {
        public override object GetPropertyOwner(PropertyDescriptor? pd)=>v;
        public override PropertyDescriptorCollection GetProperties()=>GetProperties(null);
        public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes)
        {
            string[] names=v.Type switch
            {
                RecordingEventType.MouseMove=>["Type","DelayMs","X","Y"],
                RecordingEventType.MouseDown or RecordingEventType.MouseUp=>["Type","DelayMs","X","Y","Button"],
                RecordingEventType.MouseWheel=>["Type","DelayMs","X","Y","Delta"],
                RecordingEventType.KeyDown or RecordingEventType.KeyUp=>["Type","DelayMs","Key","Scan","Extended"],
                _=>["Type","DelayMs"]
            };
            return new PropertyDescriptorCollection(TypeDescriptor.GetProperties(v).Cast<PropertyDescriptor>()
                .Where(p=>names.Contains(p.Name)).ToArray());
        }
    }

    public sealed class EventValues
    {
        [DisplayName("事件类型"),TypeConverter(typeof(EventTypes))] public string Type{get;set;}
        [DisplayName("等待（毫秒）")] public int DelayMs{get;set;}
        [DisplayName("横坐标")] public int X{get;set;}
        [DisplayName("纵坐标")] public int Y{get;set;}
        [DisplayName("鼠标键：left / middle / right")] public string? Button{get;set;}
        [DisplayName("滚轮增量（通常 120）")] public int Delta{get;set;}
        [DisplayName("键盘按键")] public Keys Key{get;set;}
        [DisplayName("扫描码（0 自动）")] public ushort Scan{get;set;}
        [DisplayName("扩展键")] public bool Extended{get;set;}
        public EventValues(RecordedEvent e){Type=e.Type;DelayMs=e.DelayMs;X=e.X;Y=e.Y;Button=e.Button;Delta=e.Delta;Key=(Keys)e.Vk;Scan=e.Scan;Extended=e.Extended;}

        // Zero the fields the given type does not use, so ToEvent never emits a mixed
        // event. X/Y are kept for every mouse type; keyboard state is kept when the
        // target type is keyboard (switching to keyboard with an invalid empty key
        // falls back to A so the event is immediately valid).
        public void NormalizeForType(string type)
        {
            bool mouse=RecordingEventType.IsMouse(type);
            bool pressRelease=type is RecordingEventType.MouseDown or RecordingEventType.MouseUp;
            bool key=type is RecordingEventType.KeyDown or RecordingEventType.KeyUp;
            if(!mouse){X=0;Y=0;}
            Button=pressRelease&&mouse?(Button??"left"):null;
            Delta=type==RecordingEventType.MouseWheel?Delta:0;
            if(!key)
            {
                Key=Keys.None;Scan=0;Extended=false;
            }
            else if(Key==Keys.None){Key=Keys.A;Scan=0;}
        }

        public RecordedEvent ToEvent()=>new(){Type=Type,DelayMs=DelayMs,X=YieldX(),Y=YieldY(),Button=Button,Delta=Delta,Vk=(ushort)Key,Scan=Scan,Extended=Extended};
        private int YieldX()=>RecordingEventType.IsMouse(Type)?X:0;
        private int YieldY()=>RecordingEventType.IsMouse(Type)?Y:0;
    }
    private sealed class EventTypes:StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext? c)=>true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? c)=>true;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? c)=>new(RecordingEventType.All.ToArray());
    }
}
