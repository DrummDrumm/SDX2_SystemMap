namespace SDX2_SystemMap;

/// <summary>
/// Notices when the planned route should be dropped: you got out of the cockpit or seat, or died / respawned
/// (a new character). Game-free so it can be tested; the plugin feeds it every frame.
/// </summary>
public sealed class SeatWatch
{
    private bool seen, wasSeated, wasDead;
    private long lastCharacter;

    /// <summary>
    /// seated = controlling a cockpit, seat or remote control; character = your character's entity id (0 = none);
    /// dead = your character is dead. Returns why the route should be cancelled, or null.
    /// </summary>
    public string Update(bool seated, long character, bool dead)
    {
        string why = null;
        if (seen)
        {
            if (dead && !wasDead || character != 0 && lastCharacter != 0 && character != lastCharacter) why = "you respawned";
            else if (wasSeated && !seated) why = "you left the seat";
        }
        seen = true;
        wasSeated = seated; wasDead = dead;
        if (character != 0) lastCharacter = character;
        return why;
    }

    /// <summary>Forget everything (a new session).</summary>
    public void Reset() { seen = wasSeated = wasDead = false; lastCharacter = 0; }
}
