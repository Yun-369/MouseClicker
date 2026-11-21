using MouseClicker.Actions;
using MouseClicker.Domain;
namespace MouseClicker.Platform;

// Converts a bound recording's client pixels at each event. Cleanup bypasses guards.
// A macro uses the same guard with screen coordinates after it has converted its
// client coordinates itself; screenCoordinates keeps that second conversion from
// happening while retaining the actual-cursor check at button down/up.
public sealed class BoundInputDriver(IInputDriver inner,WindowTargetInfo target,WindowBinding binding,
    bool screenCoordinates = false):IInputDriver
{
    private Point? clientPoint, movedTo;
    public bool UsesScreenCoordinates => screenCoordinates;
    public Point? CursorPosition=>inner.CursorPosition;
    public void Check()
    {
        if(!WindowTargetResolver.EnsureUsable(target,binding,out var error))throw new InvalidDataException(error);
        if(!WindowTargetResolver.IsForeground(target.Hwnd))throw new InvalidDataException("目标窗口已不在前台，回放停止。");
    }
    public void MoveTo(int x,int y)
    {
        Check();
        if (screenCoordinates)
        {
            movedTo = new Point(x, y);
            clientPoint = null;
            inner.MoveTo(x, y);
            return;
        }
        if(x<0||y<0||x>=binding.ClientWidth||y>=binding.ClientHeight)throw new InvalidDataException("坐标超出绑定客户区。");
        if(!WindowTargetResolver.ClientToScreenPoint(target.Hwnd,x,y,out int sx,out int sy))throw new InvalidDataException("无法换算客户区坐标。");
        clientPoint=new Point(x,y);movedTo=new Point(sx,sy);inner.MoveTo(sx,sy);
    }
    // Macro path: retain the original client point while sending its current
    // screen conversion.  ButtonDown/ButtonUp can then detect a window move
    // between conversion and the actual input event.
    public Point MoveClient(int x, int y)
    {
        Check();
        if(x<0||y<0||x>=binding.ClientWidth||y>=binding.ClientHeight)
            throw new InvalidDataException("坐标超出绑定客户区。");
        if(!WindowTargetResolver.ClientToScreenPoint(target.Hwnd,x,y,out int sx,out int sy))
            throw new InvalidDataException("无法换算客户区坐标。");
        clientPoint = new Point(x,y);
        movedTo = new Point(sx,sy);
        inner.MoveTo(sx,sy);
        return movedTo.Value;
    }
    public void ButtonDown(ClickButton button)
    {
        Check();
        if (movedTo is not {} expected) throw new InvalidDataException("缺少点击位置。");
        if (clientPoint is {} p &&
            (!WindowTargetResolver.ClientToScreenPoint(target.Hwnd,p.X,p.Y,out int x,out int y)
             || new Point(x,y)!=expected))
            throw new InvalidDataException("窗口位置已变化，未点击。");
        EnsureActualCursor(expected, "点击位置");
        inner.ButtonDown(button);
    }
    public void ButtonUp(ClickButton button)
    {
        Check();
        if (movedTo is {} expected)
        {
            if (clientPoint is {} p &&
                (!WindowTargetResolver.ClientToScreenPoint(target.Hwnd,p.X,p.Y,out int x,out int y)
                 || new Point(x,y)!=expected))
                throw new InvalidDataException("窗口位置已变化，未抬起鼠标。");
            EnsureActualCursor(expected, "鼠标抬起位置");
        }
        inner.ButtonUp(button);
    }
    public void KeyDown(ushort vk,bool extended=false,ushort scan=0){Check();inner.KeyDown(vk,extended,scan);}
    public void KeyUp(ushort vk,bool extended=false,ushort scan=0){Check();inner.KeyUp(vk,extended,scan);}
    public void UnicodeKey(ushort codeUnit,bool down){Check();inner.UnicodeKey(codeUnit,down);}
    public void Wheel(int delta,int x,int y)
    {
        MoveTo(x,y);
        if(movedTo is {} p)
        {
            EnsureActualCursor(p, "滚轮位置");
            inner.Wheel(delta,p.X,p.Y);
        }
    }
    public void ReleaseAll()=>inner.ReleaseAll();

    private void EnsureActualCursor(Point expected, string what)
    {
        if(inner.CursorPosition is not {} actual || actual!=expected)
            throw new InvalidDataException($"{what}的实际鼠标位置已变化或无法读取，未发送输入。");
        if(!WindowTargetResolver.IsPointOverTarget(target.Hwnd,actual.X,actual.Y,out var error,what))
            throw new InvalidDataException(error);
    }
}
