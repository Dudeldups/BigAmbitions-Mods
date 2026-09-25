#nullable enable
using Entities;

namespace BigHax
{
    internal sealed class BigHaxPurchaseLimitService
    {
        public bool AreLimitsDisabled()
        {
            return SaveGameManager.Current?.gameVariables?.disableWholesaleAndImportLimits == true;
        }

        public void SetLimitsDisabled(bool disabled)
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame?.gameVariables == null)
                return;

            saveGame.gameVariables.disableWholesaleAndImportLimits = disabled;
            saveGame.hasEverUsedMods = true;
            SaveGameManager.MarkChange();
        }
    }
}
