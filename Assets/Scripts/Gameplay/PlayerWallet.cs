using System;
using UnityEngine;

[Serializable]
public struct WalletSnapshot
{
    public int balance, totalEarned, totalSpent;
}

public readonly struct WalletTransaction
{
    public readonly int delta, balance;
    public readonly string reason;
    public WalletTransaction(int delta,int balance,string reason)
    { this.delta=delta; this.balance=balance; this.reason=reason; }
}

/// <summary>Session economy boundary. Shops and rewards use transactions rather than editing a HUD counter.</summary>
[DisallowMultipleComponent]
public sealed class PlayerWallet : MonoBehaviour
{
    [SerializeField, Min(0)] int startingCoins;
    public int Balance { get; private set; }
    public int TotalEarned { get; private set; }
    public int TotalSpent { get; private set; }
    public event Action<WalletTransaction> Changed;
    void Awake() => Balance=Mathf.Max(0,startingCoins);
    public bool CanAfford(int amount) => amount>=0 && Balance>=amount;
    public bool TryCredit(int amount,string reason="Reward")
    {
        if(amount<=0 || amount>int.MaxValue-Balance || amount>int.MaxValue-TotalEarned) return false;
        Balance+=amount; TotalEarned+=amount;
        Changed?.Invoke(new WalletTransaction(amount,Balance,reason)); return true;
    }
    public bool TrySpend(int amount,string reason="Purchase")
    {
        if(amount<=0 || !CanAfford(amount) || amount>int.MaxValue-TotalSpent) return false;
        Balance-=amount; TotalSpent+=amount;
        Changed?.Invoke(new WalletTransaction(-amount,Balance,reason)); return true;
    }
    public WalletSnapshot Capture() => new WalletSnapshot {balance=Balance,totalEarned=TotalEarned,totalSpent=TotalSpent};
    public bool Restore(WalletSnapshot snapshot)
    {
        if(snapshot.balance<0 || snapshot.totalEarned<0 || snapshot.totalSpent<0) return false;
        int delta=snapshot.balance-Balance;
        Balance=snapshot.balance;TotalEarned=snapshot.totalEarned;TotalSpent=snapshot.totalSpent;
        Changed?.Invoke(new WalletTransaction(delta,Balance,"Restore")); return true;
    }
}
