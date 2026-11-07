using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Documents;

namespace ReFix.QuickNote
{
    public sealed class NoteRecord
    {
        public string Id { get; set; }
        public string ListId { get; set; }
        public string Name { get; set; }
        public bool Markdown { get; set; }
        public string TopHint { get; set; }
        public string BottomHint { get; set; }
    }

    public sealed class NoteList
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public bool Collapsed { get; set; }
        public string LastNoteId { get; set; }
    }

    public sealed class NoteIndex
    {
        public int SchemaVersion { get; set; }
        public NotePageOptions PageOptions { get; set; }
        public List<NoteRecord> Notes { get; set; }
        public List<NoteList> Lists { get; set; }
        public List<string> Colors { get; set; }
    }

    internal sealed class Library
    {
        readonly string folder, indexPath;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
        internal readonly NoteIndex Index;

        internal Library(string root)
        {
            folder = Path.Combine(root, "notes");
            indexPath = Path.Combine(folder, "library.json");
            if (File.Exists(indexPath))
            {
                Index = json.Deserialize<NoteIndex>(File.ReadAllText(indexPath, Encoding.UTF8));
                if (Index == null || Index.SchemaVersion != 1 || Index.Lists == null || Index.Notes == null || Index.Colors == null || Index.Lists.Count == 0)
                    throw new InvalidDataException("Повреждён список заметок.");
                return;
            }
            Index = new NoteIndex { SchemaVersion = 1, Lists = new List<NoteList>(), Notes = new List<NoteRecord>(), Colors = new List<string>() };
            Index.Lists.Add(new NoteList { Id = Guid.NewGuid().ToString("N"), Name = "Заметки" });
            string legacy = Path.Combine(folder, "quick-note.txt");
            if (File.Exists(legacy))
            {
                var note = NewRecord(Index.Lists[0], "Быстрая заметка");
                var document = new FlowDocument(new Paragraph(new Run(File.ReadAllText(legacy, Encoding.UTF8))));
                SaveBody(note, document);
                Index.Notes.Add(note);
                Save();
            }
        }

        static NoteRecord NewRecord(NoteList list, string name)
        {
            return new NoteRecord { Id = Guid.NewGuid().ToString("N"), Name = name, ListId = list.Id, TopHint = "", BottomHint = "" };
        }

        internal NoteRecord AddNote(NoteList list, bool markdown = false)
        {
            var note = NewRecord(list, markdown ? "Новая заметка.md" : "Новая заметка");
            note.Markdown = markdown;
            Index.Notes.Add(note);
            try { Save(); }
            catch { Index.Notes.Remove(note); throw; }
            return note;
        }

        internal NoteList AddList(string name)
        {
            var list = new NoteList { Id = Guid.NewGuid().ToString("N"), Name = name };
            Index.Lists.Add(list);
            try { Save(); }
            catch { Index.Lists.Remove(list); throw; }
            return list;
        }

        internal void Move(NoteRecord note, NoteList list)
        {
            string previous = note.ListId;
            note.ListId = list.Id;
            try { Save(); }
            catch { note.ListId = previous; throw; }
        }

        internal void Save() { Atomic(indexPath, stream => { using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true)) writer.Write(json.Serialize(Index)); }); }

        string BodyPath(NoteRecord note)
        {
            Guid id;
            if (!Guid.TryParseExact(note.Id, "N", out id)) throw new InvalidDataException("Недопустимый идентификатор заметки.");
            return Path.Combine(folder, id.ToString("N") + ".note.json");
        }

        internal void LoadBody(NoteRecord note, FlowDocument document)
        {
            string path = BodyPath(note);
            if (File.Exists(path))
                NoteDocument.Restore(document, json.Deserialize<List<NoteParagraph>>(File.ReadAllText(path, Encoding.UTF8)));
        }

        internal void SaveBody(NoteRecord note, FlowDocument document)
        {
            string path = BodyPath(note);
            Checkpoint(note, false);
            Atomic(path, stream => { using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true)) writer.Write(json.Serialize(NoteDocument.Capture(document))); });
        }

        internal void Checkpoint(NoteRecord note, bool force)
        {
            string path = BodyPath(note);
            if (File.Exists(path))
            {
                var previous = History(note);
                if (force || previous.Length == 0 || DateTime.UtcNow - File.GetCreationTimeUtc(previous[0]) > TimeSpan.FromSeconds(30))
                {
                    string snapshot = path + ".history-" + DateTime.UtcNow.Ticks;
                    File.Copy(path, snapshot, false);
                    File.SetCreationTimeUtc(snapshot, DateTime.UtcNow);
                    long used = new FileInfo(snapshot).Length;
                    for (int i = 0; i < previous.Length; i++)
                    {
                        used += new FileInfo(previous[i]).Length;
                        if (i >= 19 || used > 100L * 1024 * 1024) File.Delete(previous[i]);
                    }
                }
            }
        }

        internal string[] History(NoteRecord note)
        {
            if (!Directory.Exists(folder)) return new string[0];
            return Directory.GetFiles(folder, Path.GetFileName(BodyPath(note)) + ".history-*").OrderByDescending(path => path, StringComparer.Ordinal).ToArray();
        }

        internal void ReadHistory(string path, FlowDocument document)
        {
            NoteDocument.Restore(document, json.Deserialize<List<NoteParagraph>>(File.ReadAllText(path, Encoding.UTF8)));
        }

        internal string AddImage(NoteRecord note, byte[] bytes, string extension)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(extension ?? "", @"^\.[a-zA-Z0-9]{1,5}$")) extension = ".png";
            string name = Guid.NewGuid().ToString("N") + extension.ToLowerInvariant();
            string directory = BodyPath(note) + ".images";
            Directory.CreateDirectory(directory);
            Atomic(Path.Combine(directory, name), stream => stream.Write(bytes, 0, bytes.Length));
            return name;
        }

        internal byte[] GetImage(NoteRecord note, string name)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name ?? "", @"^[a-f0-9]{32}\.[a-z0-9]{1,5}$")) return null;
            string path = Path.Combine(BodyPath(note) + ".images", name);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        void Atomic(string path, Action<Stream> write)
        {
            Directory.CreateDirectory(folder);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) write(stream);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
