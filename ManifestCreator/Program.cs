using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

using var md5 = MD5.Create();

var options = ParseArgs(args);
var manifestPath = $"../client/{options.Target}_manifest.xml";

if (!File.Exists(manifestPath)) throw new FileNotFoundException($"manifest '{manifestPath}' not found!");

var releasesList = ReadManifest(manifestPath);
if (releasesList.Any(s => s.Version.Equals(options.Version)))
{
    Console.WriteLine("a release with version '{0}' already exists!", options.Version);
    return;
}

var deployFolder = new DirectoryInfo(Path.GetDirectoryName(manifestPath));
var osTarget = manifestPath.Replace("_manifest.xml", "");

var currentRelease = CreateReleaseFromFolder(options.CuoBinPath, options.Version, options.Name, true);

releasesList.ForEach(s => s.IsLatest = false);
releasesList.Add(currentRelease);

UpdateDiffFolders(currentRelease, deployFolder, osTarget, options.CuoBinPath);
WriteManifest("./manifest.xml", releasesList);

Console.WriteLine("Manifest created!");


List<ManifestRelease> ReadManifest(string manifestPath)
{
    Console.WriteLine("reading manifest at {0}", manifestPath);

    var list = new List<ManifestRelease>();
    if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
    {
        Console.WriteLine("manifest not found!");
        return list;
    }

    var doc = new XmlDocument();
    doc.Load(manifestPath);

    var root = doc?["releases"];

    if (root == null)
    {
        Console.WriteLine("corrupted manifest");
        return list;
    }

    list.AddRange(root.GetElementsByTagName("release")
        .OfType<XmlElement>()
        .Select(s => new ManifestRelease(s))
    );

    Console.WriteLine("releases found: {0}", list.Count);
    foreach (var release in list)
        Console.WriteLine("  {0} - {1} {2}", release.Name, release.Version, release.IsLatest ? "[latest]" : "");

    return list;
}

ManifestRelease CreateReleaseFromFolder(DirectoryInfo cuoOutputPath, string version, string name, bool isLatest)
{
    Console.WriteLine("creating release from {0}", cuoOutputPath);

    if (!cuoOutputPath.Exists) throw new DirectoryNotFoundException($"CUO Path is invalid {cuoOutputPath.FullName}");

    var fileList = new List<HashFile>();
    foreach (var f in cuoOutputPath.GetFiles("*.*", SearchOption.AllDirectories).OrderBy(s => s.FullName))
    {
        var path = f.FullName.Remove(0, cuoOutputPath.FullName.Length);
        if (path.StartsWith(Path.DirectorySeparatorChar))
        {
            path = path.Remove(0, 1);
        }

        var hash = CalculateMD5(f.FullName);
        var hashFile = new HashFile(path, hash, $"{hash[^2..]}/{f.FullName}_{hash}");
        fileList.Add(hashFile);
        Console.WriteLine(hashFile);
    }

    Console.WriteLine("done");

    return new ManifestRelease(version, name, fileList, isLatest);
}

void WriteManifest(string manifestName, List<ManifestRelease> releases)
{
    Console.WriteLine("saving manifest {0}", manifestName);

    var fs = File.CreateText(manifestName);
    using var xml = new XmlTextWriter(fs.BaseStream, Encoding.UTF8)
    {
        Formatting = Formatting.Indented,
        IndentChar = '\t',
        Indentation = 1
    };

    xml.WriteStartDocument(true);
    xml.WriteStartElement("releases");
    releases.ForEach(s => s.Save(xml));
    xml.WriteEndElement();
    xml.WriteEndDocument();

    xml.Flush();

    Console.WriteLine("done");
}

string CalculateMD5(string filename)
{
    using var stream = File.OpenRead(filename);
    var hash = md5.ComputeHash(stream);
    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
}

void UpdateDiffFolders(ManifestRelease release, DirectoryInfo deployFolder, string osVersion, DirectoryInfo cuoBinPath)
{
    //var releaseFolder = cuoBinPath ?? new DirectoryInfo(Path.Combine(deployFolder.FullName, osVersion));
    var diffFolder = new DirectoryInfo(Path.Combine(deployFolder.FullName, "diff"));

    foreach (var file in release.Files.Where(s => !string.IsNullOrWhiteSpace(s.Hash)))
    {
        var dFolder = Directory.CreateDirectory(Path.Combine(diffFolder.FullName, file.Hash[^2..]));
        var dFilePath = new FileInfo(Path.Combine(dFolder.FullName, file.Filename));

        Console.WriteLine("folder name {0} {1}", Path.Combine(dFolder.Name, file.Filename), file.Hash);

        if (!dFilePath.Exists)
        {
            if (dFilePath.Directory != null && !dFilePath.Directory.Exists)
                dFilePath.Directory.Create();

            var srcFile = new FileInfo(Path.Combine(cuoBinPath.FullName, file.Filename));
            if (!srcFile.Exists)
            {
                Console.WriteLine("file not exists! {0}", srcFile.FullName);
                continue;
            }

            var hash = CalculateMD5(srcFile.FullName);
            if (!hash.Equals(file.Hash))
            {
                Console.WriteLine("hash is different!");
                continue;
            }

            File.Copy(srcFile.FullName, dFilePath.FullName + "_" + file.Hash, true);
        }

        file.Url = Path.Combine(dFolder.Name, file.Filename + "_" + file.Hash)
            .Replace('\\', '/');
    }
}

Options ParseArgs(string[] args)
{
    var cuoPath = string.Empty;
    var version = string.Empty;
    var name = string.Empty;
    var target = string.Empty;

    for (int i = 0; i < args.Length; ++i)
    {
        var cmd = args[i];

        if (!cmd.StartsWith("--"))
            continue;

        switch (cmd[2..])
        {
            case "bin":
                cuoPath = args[i + 1];
                break;

            case "version":
                version = args[i + 1];
                break;

            case "name":
                name = args[i + 1];
                break;

            case "target":
                target = args[i + 1];
                break;
        }
    }

    return new Options(new DirectoryInfo(cuoPath), version, name, target);
}

sealed record Options
(
    DirectoryInfo CuoBinPath,
    string Version,
    string Name,
    string Target
);

sealed class HashFile
{
    public HashFile(string fileName, string hash, string url)
    {
        Filename = fileName;
        Hash = hash;
        Url = url;
    }

    public string Filename { get; }
    public string Url { get; set; }
    public string Hash { get; }

    public void Save(XmlWriter xml)
    {
        xml.WriteStartElement("file");
        xml.WriteAttributeString("filename", Filename);
        xml.WriteAttributeString("hash", Hash);
        xml.WriteAttributeString("url", Url);
        xml.WriteEndElement();
    }

    public override string ToString()
    {
        return $"{Filename} - {Hash}";
    }
}


sealed class ManifestRelease
{
    public string Version { get; }
    public string Name { get; }
    public List<HashFile> Files { get; }
    public bool IsLatest { get; set; }

    public ManifestRelease(string version ,string name, List<HashFile> files, bool isLatest)
    {
        Version = version;
        Name = name;
        Files = files;
        IsLatest = isLatest;
    }

    public ManifestRelease(XmlElement xml)
    {
        Name = xml.GetAttribute("name");
        Version = xml.GetAttribute("version");
        bool.TryParse(xml.GetAttribute("latest"), out var res);
        IsLatest = res;
        Files ??= new List<HashFile>();

        foreach (XmlElement element in xml["files"].GetElementsByTagName("file"))
            Files.Add(new HashFile(element.GetAttribute("filename"), element.GetAttribute("hash"), element.GetAttribute("url")));

        Files.Sort((a, b) => a.Filename.CompareTo(b.Filename));
    }

    public void Save(XmlWriter xml)
    {
        xml.WriteStartElement("release");
        xml.WriteAttributeString("name", Name);
        xml.WriteAttributeString("version", Version);
        xml.WriteAttributeString("latest", IsLatest.ToString());

        xml.WriteStartElement("files");
        foreach (var file in Files)
        {
            file.Save(xml);
        }
        xml.WriteEndElement();

        xml.WriteEndElement();
    }
}