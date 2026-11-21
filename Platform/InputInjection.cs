namespace MouseClicker.Platform;

// Marker stamped into dwExtraInfo of every SendInput event we emit. Low-level
// hooks use it to recognise (and ignore) our own injected events, so keyboard
// playback can never re-trigger the hold-to-run hotkey and recordings never
// capture what the program itself plays back. Other injectors (macro tools,
// on-screen keyboards) keep their own extra info and are still accepted.
internal static class InputInjection
{
    // "MCKR"; arbitrary, only has to be unique inside this app. Fits in 32 bits.
    public static readonly nuint Signature = 0x4D43_4B52u;
}
