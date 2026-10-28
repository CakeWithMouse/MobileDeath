using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Blinopek.Application;
using UnityEngine;

namespace Blinopek.Infrastructure
{
    public sealed class FileGameStore : IResettableGameStore
    {
        private sealed class SaveVersionException : IOException { public SaveVersionException(string message) : base(message) { } }
        [Serializable] private sealed class Envelope { public string checksum; public string payload; }
        private readonly string path;
        public string RecoveryNotice { get; private set; }
        public FileGameStore(string path) { this.path = path; }
        private sealed class FileSnapshot { public string Text; public Exception Error; }
        private System.Collections.Generic.Dictionary<string, FileSnapshot> prefetched;
        public System.Threading.Tasks.Task PrefetchAsync()
        {
            return System.Threading.Tasks.Task.Run(() =>
            {
                var files = new System.Collections.Generic.Dictionary<string, FileSnapshot>();
                foreach (var file in new[] { path, path + ".bak" })
                {
                    var snapshot = new FileSnapshot(); files[file] = snapshot;
                    try { snapshot.Text = File.ReadAllText(file, Encoding.UTF8); }
                    catch (IOException ex) { snapshot.Error = ex; }
                    catch (UnauthorizedAccessException ex) { snapshot.Error = ex; }
                }
                prefetched = files;
            });
        }
        private SavedGame ReadInitial(string file)
        {
            if (prefetched != null && prefetched.TryGetValue(file, out var snapshot))
            {
                if (snapshot.Error != null) throw snapshot.Error;
                return Parse(snapshot.Text);
            }
            return Read(file);
        }
        public SavedGame Load()
        {
            try { return LoadCore(); }
            finally { prefetched = null; }
        }
        private SavedGame LoadCore()
        {
            if (!File.Exists(path) && !File.Exists(path + ".bak")) return null;
            Exception primary;
            try { return ReadInitial(path); } catch (SaveVersionException) { throw; } catch (Exception ex) { primary = ex; }
            try { var backup = ReadInitial(path + ".bak"); RecoveryNotice = "Восстановлена предыдущая целая копия сохранения."; return backup; }
            catch { throw new IOException("Основная и резервная копии недоступны. Файлы сохранены для восстановления.", primary); }
        }
        private static string Hash(string input)
        {
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(input)));
        }
        private static SavedGame Read(string file)
        {
            return Parse(File.ReadAllText(file, Encoding.UTF8));
        }
        private static SavedGame Parse(string text)
        {
            var envelope = JsonUtility.FromJson<Envelope>(text);
            if (envelope == null || envelope.payload == null || envelope.checksum != Hash(envelope.payload)) throw new InvalidDataException("Checksum mismatch.");
            var game = JsonUtility.FromJson<SavedGame>(envelope.payload);
            if (game != null && (game.schema != 1 || game.environment != "local-development"))
                throw new SaveVersionException("Сохранение другой версии или окружения. Его нельзя заменить текущей версией игры.");
            if (game == null || game.profile == null || game.rules == null || game.commands == null)
                throw new InvalidDataException("Unsupported save format.");
            // Unity serializes inline null classes as objects with zeroed fields.
            // Keep an explicit marker for a profile saved before its first new session.
            if (game.awaitingDay) {
                if (game.commands.Count != 0 || game.settled) throw new InvalidDataException("Unexpected session in a fresh save.");
                game.sessionStart = null;
            }
            game.rules.Validate();
            return game;
        }
        private string WriteTemporary(SavedGame game)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string payload = JsonUtility.ToJson(game), temp = path + ".writing";
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Envelope { payload = payload, checksum = Hash(payload) }));
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            Read(temp);
            return temp;
        }
        public void Reset(SavedGame game)
        {
            string temp = WriteTemporary(game);
            // Remove the old fallback before committing, so reset progress cannot reappear.
            // If this or the atomic replacement fails, the current primary stays untouched.
            File.Delete(path + ".bak");
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
            RecoveryNotice = null;
        }
        public void Save(SavedGame game)
        {
            string temp = WriteTemporary(game);
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
