namespace Versionary;

/// <summary>Which way along the chain a hop went.</summary>
public enum MigrationDirection
{
    /// <summary>Towards the current contract — an old message being brought up to date.</summary>
    Forward = 0,

    /// <summary>Back towards an older contract — a result being taken down to what the caller asked for.</summary>
    Backward = 1,
}
