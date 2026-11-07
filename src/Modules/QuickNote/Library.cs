using System;
using System.Collections.Generic;
using System.IO;
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
    }

    public sealed class NoteIndex
    {
        public int SchemaVersion { get; set; }
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

        internal NoteRecord AddNote(NoteList list)
        {
            var note = NewRecord(list, "Новая заметка");
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
            Atomic(BodyPath(note), stream => { using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true)) writer.Write(json.Serialize(NoteDocument.Capture(document))); });
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
