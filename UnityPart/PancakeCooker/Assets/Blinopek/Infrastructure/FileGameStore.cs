using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Blinopek.Application;
using UnityEngine;

namespace Blinopek.Infrastructure
{
    public sealed class FileGameStore : IGameStore
    {
        private sealed class SaveVersionException : IOException { public SaveVersionException(string message) : base(message) { } }
        [Serializable] private sealed class Envelope { public string checksum; public string payload; }
        private readonly string path;
        public string RecoveryNotice { get; private set; }
        public FileGameStore(string path) { this.path = path; }
        public SavedGame Load()
        {
            if (!File.Exists(path) && !File.Exists(path + ".bak")) return null;
            Exception primary;
            try { return Read(path); } catch (SaveVersionException) { throw; } catch (Exception ex) { primary = ex; }
            try { var backup = Read(path + ".bak"); RecoveryNotice = "Восстановлена предыдущая целая копия сохранения."; return backup; }
            catch { throw new IOException("Основная и резервная копии недоступны. Файлы сохранены для восстановления.", primary); }
        }
        private static string Hash(string input)
        {
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(input)));
        }
        private static SavedGame Read(string file)
        {
            var envelope = JsonUtility.FromJson<Envelope>(File.ReadAllText(file, Encoding.UTF8));
            if (envelope == null || envelope.payload == null || envelope.checksum != Hash(envelope.payload)) throw new InvalidDataException("Checksum mismatch.");
            var game = JsonUtility.FromJson<SavedGame>(envelope.payload);
            if (game != null && (game.schema != 1 || game.environment != "local-development"))
                throw new SaveVersionException("Сохранение другой версии или окружения. Его нельзя заменить текущей версией игры.");
            if (game == null || game.profile == null || game.rules == null || game.commands == null)
                throw new InvalidDataException("Unsupported save format.");
            game.rules.Validate();
            return game;
        }
        public void Save(SavedGame game)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string payload = JsonUtility.ToJson(game), temp = path + ".writing";
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Envelope { payload = payload, checksum = Hash(payload) }));
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            Read(temp);
            if (File.Exists(path))
            {
                // Never replace a good backup with the corrupt primary after recovery.
                bool valid = false; try { Read(path); valid = true; } catch (SaveVersionException) { throw; } catch { }
                if (valid) File.Replace(temp, path, path + ".bak");
                else {
                    File.Move(path, path + ".corrupt-" + DateTime.UtcNow.Ticks);
                    File.Move(temp, path);
                }
            }
            else File.Move(temp, path);
        }
    }
}
