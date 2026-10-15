using System;
using System.Text.RegularExpressions;

namespace Blinopek.Application
{
    public sealed partial class GameController
    {
        public const string DefaultCafeName = "Блинопёк";
        public const int CafeNameMaxLength = 32;
        public string CafeName => string.IsNullOrWhiteSpace(saved.cafeName) ? DefaultCafeName : saved.cafeName;

        public static bool ValidateCafeName(string value, out string normalized, out string error)
        {
            normalized = Regex.Replace(value ?? "", @"\s+", " ").Trim();
            error = normalized.Length == 0 ? "Введите название блинной." :
                normalized.Length > CafeNameMaxLength ? "Название должно быть не длиннее 32 символов." : null;
            if (error == null)
                foreach (char c in normalized)
                    if (char.IsControl(c)) { error = "Уберите непечатные символы из названия."; break; }
            return error == null;
        }

        public bool RenameCafe(string value, out string error)
        {
            if (!ValidateCafeName(value, out string normalized, out error)) return false;
            if (StorageBlocked) { error = "Сохранение недоступно. Название пока нельзя изменить."; return false; }
            // Save a new envelope first: failed writes must not change the visible name.
            var renamed = saved.WithCafeName(normalized);
            try { store.Save(renamed); }
            catch (Exception ex) {
                StorageError = "Не удалось сохранить название: " + ex.Message;
                error = "Не удалось сохранить название. Проверьте свободное место и попробуйте ещё раз.";
                Changed?.Invoke(); return false;
            }
            saved = renamed; StorageError = null; Changed?.Invoke(); return true;
        }
    }
}
