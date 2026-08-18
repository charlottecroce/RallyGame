namespace RallyGame.Core
{
    public enum DamageType { Impact, Wear, Overheat }

    public interface IDamageable { void ApplyDamage(float amount, DamageType type); }

    public interface IRepairable
    {
        float Condition { get; }   // 0 = destroyed, 1 = pristine
        float RepairCost { get; }
        void Repair();
    }

    /// Anything the first-person raycast can target.
    public interface IInteractable
    {
        string Prompt { get; }
        bool CanInteract { get; }
        void Interact(UnityEngine.GameObject instigator);
    }
}
