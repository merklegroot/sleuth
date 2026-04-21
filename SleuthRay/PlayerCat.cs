namespace SleuthRay;

internal enum PlayerCatState
{
    Held = 0,
    InFlight = 1,
    Deployed = 2,
}

internal struct PlayerCat
{
    public int Id;
    public string Name;
    public float Health;
    public int MaxHealth;
    public int SpriteVariant;
    public PlayerCatState State;
    public float HeldHealTimer;
}

