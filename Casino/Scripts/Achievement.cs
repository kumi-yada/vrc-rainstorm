
// Achievement identifiers for the rainstorm world.
//
// Each value is a BIT INDEX, not a mask. Every achievement is packed into a
// single int saved in PlayerData (see Achievements.UnlockedKey), exactly like
// CardTheme's unlocked-flags bitmask: achievement n lives at bit n. Because the
// mask is a signed int the sign bit is off limits, so at most 31 achievements
// (bits 0..30) can coexist. Appending new values at the end is safe; reordering
// or removing existing values would move saved bits to the wrong achievement,
// so treat existing numbers as frozen once the world has shipped.
//
// Rename or extend the entries below to match your actual achievements. The
// manager does not care what they are called, only about the zero-based order.
public enum Achievement
{
    FirstGame = 0,
    FirstWin = 1,
    SpiderWin = 2,
    CanfieldWin = 3,
    TenWins = 4,
    HundredWins = 5,
    BigSpender = 6,
    CardCollector = 7,
}
