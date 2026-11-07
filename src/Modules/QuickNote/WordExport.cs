using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace ReFix.QuickNote
{
    internal sealed class WordExport
    {
        static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
        static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
        readonly XElement relationships = new XElement(Rel+"Relationships");
        readonly List<XElement> definitions = new List<XElement>(), instances = new List<XElement>();
        ZipArchive zip;
        int images;

        internal void Write(Stream output, List<NoteParagraph> blocks)
        {
            using (zip = new ZipArchive(output,ZipArchiveMode.Create,true))
            {
                var body = new XElement(W+"body");
                Blocks(body,blocks,0,0);
                body.Add(new XElement(W+"sectPr", new XElement(W+"pgSz", new XAttribute(W+"w",11906), new XAttribute(W+"h",16838)), new XElement(W+"pgMar", new XAttribute(W+"top",1440), new XAttribute(W+"right",1440), new XAttribute(W+"bottom",1440), new XAttribute(W+"left",1440), new XAttribute(W+"header",720), new XAttribute(W+"footer",720), new XAttribute(W+"gutter",0))));
                Part("word/document.xml", new XElement(W+"document", new XAttribute(XNamespace.Xmlns+"w",W.NamespaceName), new XAttribute(XNamespace.Xmlns+"r",R.NamespaceName), body));
                Part("_rels/.rels",new XElement(Rel+"Relationships",Relationship("rId1","officeDocument","word/document.xml")));
                Part("[Content_Types].xml",new XElement(Ct+"Types",
                    Default("rels","application/vnd.openxmlformats-package.relationships+xml"), Default("xml","application/xml"), Default("png","image/png"),
                    Override("/word/document.xml","document.main"), Override("/word/numbering.xml","numbering"), Override("/word/styles.xml","styles")));
                relationships.Add(Relationship("rIdNumbering","numbering","numbering.xml"));
                relationships.Add(Relationship("rIdStyles","styles","styles.xml"));
                Part("word/_rels/document.xml.rels",relationships);
                Part("word/numbering.xml",new XElement(W+"numbering",definitions,instances));
                Part("word/styles.xml",new XElement(W+"styles",Enumerable.Range(1,6).Select(n =>
                    new XElement(W+"style",new XAttribute(W+"type","paragraph"),new XAttribute(W+"styleId","Heading"+n),Val("name","heading "+n),
                        new XElement(W+"pPr",Val("outlineLvl",n-1)),new XElement(W+"rPr",new XElement(W+"b"),Val("sz",44-n*4))))));
            }
        }

        void Blocks(XElement body, IEnumerable<NoteParagraph> blocks, int number, int depth)
        {
            bool markerPending = number != 0;
            foreach (var block in blocks)
            {
                if (block.Items != null)
                {
                    int listNumber = AddList(block.Marker,block.StartIndex,depth);
                    foreach (var item in block.Items) Blocks(body,item,listNumber,depth+1);
                    continue;
                }
                var props = new XElement(W+"pPr");
                if (block.Heading) props.Add(Val("pStyle","Heading"+HeadingLevel(block.Size)));
                string align = block.Alignment == "Justify" ? "both" : block.Alignment == "Center" ? "center" : block.Alignment == "Right" ? "right" : "left";
                if (markerPending)
                {
                    props.Add(new XElement(W+"numPr",Val("ilvl",Math.Min(8,Math.Max(0,depth-1))),Val("numId",number)));
                    markerPending = false;
                }
                props.Add(Val("jc",align));
                var paragraph = new XElement(W+"p",props);
                foreach (var part in block.Parts) paragraph.Add(Run(part));
                body.Add(paragraph);
            }
        }

        static int HeadingLevel(double size)
        {
            if (size >= 28) return 1;
            if (size >= 23) return 2;
            if (size >= 20) return 3;
            if (size >= 18) return 4;
            if (size >= 17) return 5;
            return 6;
        }

        int AddList(string marker, int start, int depth)
        {
            int id = definitions.Count + 1;
            string format = marker == "LowerLatin" ? "lowerLetter" : marker == "UpperLatin" ? "upperLetter" : marker == "LowerRoman" ? "lowerRoman" : marker == "UpperRoman" ? "upperRoman" : ExportFile.Ordered(marker) ? "decimal" : "bullet";
            var abstractNumber = new XElement(W+"abstractNum",new XAttribute(W+"abstractNumId",id),Val("multiLevelType","multilevel"));
            for (int level = 0; level < 9; level++)
            {
                string label = format == "bullet" ? (marker == "Circle" ? "○" : marker == "Square" ? "▪" : "•") : "%" + (level+1) + ".";
                abstractNumber.Add(new XElement(W+"lvl", new XAttribute(W+"ilvl",level), Val("start",Math.Max(1,start)),Val("numFmt",format),Val("lvlText",label),Val("lvlJc","left"),
                    new XElement(W+"pPr",new XElement(W+"ind",new XAttribute(W+"left",(level+1)*420),new XAttribute(W+"hanging",300)))));
            }
            definitions.Add(abstractNumber);
            instances.Add(new XElement(W+"num",new XAttribute(W+"numId",id),Val("abstractNumId",id)));
            return id;
        }

        XElement Run(NotePart part)
        {
            if (part.Break) return new XElement(W+"r",new XElement(W+"br"));
            if (part.Image != null) return Image(part.Image);
            var props = new XElement(W+"rPr");
            if (!String.IsNullOrWhiteSpace(part.Font)) props.Add(new XElement(W+"rFonts", new XAttribute(W+"ascii",part.Font), new XAttribute(W+"hAnsi",part.Font), new XAttribute(W+"cs",part.Font)));
            if (part.Weight >= 600) props.Add(new XElement(W+"b"));
            if (part.Italic) props.Add(new XElement(W+"i"));
            if (part.Strike) props.Add(new XElement(W+"strike"));
            if (ExportFile.Hex(part.Foreground) != null) props.Add(Val("color",ExportFile.Hex(part.Foreground)));
            if (part.Size > 0 && part.Size < 500) props.Add(Val("sz",(int)Math.Round(part.Size*1.5)));
            if (part.Underline) props.Add(Val("u","single"));
            if (ExportFile.Hex(part.Background) != null && !part.Background.StartsWith("#00",StringComparison.Ordinal))
                props.Add(new XElement(W+"shd",new XAttribute(W+"val","clear"),new XAttribute(W+"fill",ExportFile.Hex(part.Background))));
            var run = new XElement(W+"r",props);
            var text = part.Text ?? "";
            var lines = text.Replace("\r\n","\n").Replace("\r","\n").Split('\n');
            for (int i=0;i<lines.Length;i++)
            {
                if (i>0) run.Add(new XElement(W+"br"));
                string[] segments = lines[i].Split('\t');
                for (int j=0;j<segments.Length;j++)
                {
                    if (j>0) run.Add(new XElement(W+"tab"));
                    string valid = new string(segments[j].Where(c => XmlConvert.IsXmlChar(c) || Char.IsSurrogate(c)).ToArray());
                    run.Add(new XElement(W+"t",new XAttribute(XNamespace.Xml+"space","preserve"),valid));
                }
            }
            return run;
        }

        XElement Image(string encoded)
        {
            double width,height;
            var bytes = ExportFile.Png(encoded,out width,out height);
            int id = ++images;
            string name = "image"+id+".png", relationship = "rIdImage"+id;
            using (var stream = zip.CreateEntry("word/media/"+name).Open()) stream.Write(bytes,0,bytes.Length);
            relationships.Add(Relationship(relationship,"image","media/"+name));
            long cx=(long)(width*9525), cy=(long)(height*9525);
            var picture = new XElement(Pic+"pic",
                new XElement(Pic+"nvPicPr",new XElement(Pic+"cNvPr",new XAttribute("id",id),new XAttribute("name",name)),new XElement(Pic+"cNvPicPr")),
                new XElement(Pic+"blipFill",new XElement(A+"blip",new XAttribute(R+"embed",relationship)),new XElement(A+"stretch",new XElement(A+"fillRect"))),
                new XElement(Pic+"spPr",
                    new XElement(A+"xfrm",new XElement(A+"off",new XAttribute("x",0),new XAttribute("y",0)),new XElement(A+"ext",new XAttribute("cx",cx),new XAttribute("cy",cy))),
                    new XElement(A+"prstGeom",new XAttribute("prst","rect"),new XElement(A+"avLst"))));
            return new XElement(W+"r",new XElement(W+"drawing",new XElement(Wp+"inline",
                new XElement(Wp+"extent",new XAttribute("cx",cx),new XAttribute("cy",cy)),
                new XElement(Wp+"docPr",new XAttribute("id",id),new XAttribute("name",name)),
                new XElement(A+"graphic",new XElement(A+"graphicData",new XAttribute("uri",Pic.NamespaceName),picture)))));
        }

        void Part(string path, XElement root)
        {
            using (var stream = zip.CreateEntry(path,CompressionLevel.Optimal).Open())
            using (var writer = XmlWriter.Create(stream,new XmlWriterSettings { Encoding = new System.Text.UTF8Encoding(false), CloseOutput = false }))
                new XDocument(root).Save(writer);
        }
        static XElement Val(string key, object value) { return new XElement(W+key,new XAttribute(W+"val",value)); }
        static XElement Relationship(string id,string type,string target) { return new XElement(Rel+"Relationship",new XAttribute("Id",id),new XAttribute("Type",R.NamespaceName+"/"+type),new XAttribute("Target",target)); }
        static XElement Default(string extension,string mime) { return new XElement(Ct+"Default",new XAttribute("Extension",extension),new XAttribute("ContentType",mime)); }
        static XElement Override(string path,string type) { return new XElement(Ct+"Override",new XAttribute("PartName",path),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.wordprocessingml."+type+"+xml")); }
    }
}
