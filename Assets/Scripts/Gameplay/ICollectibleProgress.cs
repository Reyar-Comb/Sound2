using System.Collections.Generic;

/// <summary>Progress recorded by the active run or profile after a collectible is interacted with.</summary>
public interface ICollectibleProgress
{
    /// <summary>Read-only collection of stable collectible IDs already recorded by this progress provider.</summary>
    IReadOnlyCollection<string> CollectedIds { get; }

    /// <summary>Use an authored ID that remains stable across scene reloads and object renames.</summary>
    bool IsCollected(string collectibleId);

    /// <summary>Records a collection and returns true only on the first successful registration.</summary>
    bool TryCollect(string collectibleId);
}
