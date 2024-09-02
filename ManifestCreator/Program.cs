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
var deployFolder = new DirectoryInfo(Path.GetDirectoryName(manifestPath));

if (!File.Exists(manifestPath))
    Console.WriteLine("manifest '{0}' not found!", manifestPath);

var releasesList = ReadManifest(manifestPath);

// remove all files in "diff" folder that are not listed in releases
if (options.Cleanup)
{
    var targets = new []{ "linux-x64", "win-x64", "osx-x64" };
    var manifestList = new List<List<ManifestRelease>>();

    foreach (var target in targets)
    {
        var path = $"../client/{target}_manifest.xml";
        var manifestEntry = ReadManifest(path);
        manifestList.Add(manifestEntry);
    }

    Console.WriteLine("cleanup");
    var diffFolder = new DirectoryInfo("../client/diff");

    var hashes = manifestList
        .SelectMany(s => s)
        .SelectMany(s => s.Files)
        .Where(s => !string.IsNullOrEmpty(s.Hash))
        .Select(k => k.Hash.ToLowerInvariant())
        .ToHashSet();

    var diffHashes = diffFolder.GetFiles("*.*", SearchOption.AllDirectories)
        .GroupBy(s => s.Extension[(s.Extension.IndexOf('_') + 1) ..].ToLowerInvariant())
        .ToDictionary(k => k.Key, v => v.First());

    foreach ((var hash, var file) in diffHashes)
    {
        if (!hashes.Contains(hash))
        {
            if (file.Exists)
            {
                Console.WriteLine("obsolete file found: {0}", file.FullName);
                file.Delete();
            }
        }
    }

    Console.WriteLine("deleting empty folders");
    foreach (var dir in diffFolder.GetDirectories().Where(s => s.GetFiles("*.*", SearchOption.AllDirectories).Length == 0))
    {
        if (dir.Exists)
            dir.Delete(true);
    }

    Console.WriteLine("cleanup done");
}

if (releasesList.RemoveAll(s => s.Version.Equals(options.Version)) > 0)
{
    Console.WriteLine("a release with version '{0}' already exists!", options.Version);
    // return;
}

var currentRelease = CreateReleaseFromFolder(options.CuoBinPath, options.Version, options.Name, options.IsLatest);

if (currentRelease.IsLatest)
    releasesList.ForEach(s => s.IsLatest = false);
else if (releasesList.Count <= 0)
    currentRelease.IsLatest = true;

releasesList.Add(currentRelease);

UpdateDiffFolders(currentRelease, deployFolder, options.CuoBinPath);
WriteManifest(Path.Combine(options.ManifestOutput, $"{options.Target}_manifest.xml"), releasesList);

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

        path = path.Replace('\\', '/');

        var hash = CalculateMD5(f.FullName);
        var hashFile = new HashFile(path, hash, $"{HashFolder(hash)}/{f.FullName}_{hash}");
        fileList.Add(hashFile);

        Console.WriteLine(hashFile);
    }

    Console.WriteLine("done");

    return new ManifestRelease(version, name, fileList, isLatest);
}

void WriteManifest(string manifestName, List<ManifestRelease> releases)
{
    Console.WriteLine("saving manifest {0}", manifestName);

    var fileInfo = new FileInfo(manifestName);
    if (fileInfo.Directory != null && !fileInfo.Directory.Exists)
        fileInfo.Directory.Create();

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

void UpdateDiffFolders(ManifestRelease release, DirectoryInfo deployFolder, DirectoryInfo cuoBinPath)
{
    Console.WriteLine("updating diff paths");

    var diffFolder = new DirectoryInfo(Path.Combine(deployFolder.FullName, "diff"));

    foreach (var file in release.Files.Where(s => !string.IsNullOrWhiteSpace(s.Hash)))
    {
        var dFolder = Directory.CreateDirectory(Path.Combine(diffFolder.FullName, HashFolder(file.Hash)));
        var hashedFilePath = file.Filename + "_" + file.Hash;
        var dFilePath = new FileInfo(Path.Combine(dFolder.FullName, hashedFilePath));

        Console.WriteLine("hashed file path: {0}", Path.Combine(dFolder.Name, hashedFilePath));

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

            Console.WriteLine("copying {0}", dFilePath.FullName);

            File.Copy(srcFile.FullName, dFilePath.FullName, true);
        }

        file.Url = Path.Combine(dFolder.Name, hashedFilePath).Replace('\\', '/');
    }

    Console.WriteLine("done");
}

string HashFolder(string hash) => hash[^2..];

Options ParseArgs(string[] args)
{
    var cuoPath = string.Empty;
    var version = string.Empty;
    var name = string.Empty;
    var target = string.Empty;
    var latest = true;
    var output = "./";
    var cleanup = false;

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

            case "latest":
                latest = bool.Parse(args[i + 1]);
                break;

            case "output":
                output = args[i + 1];
                break;

            case "clean":
                cleanup = true;
                break;
        }
    }

    return new Options(new DirectoryInfo(cuoPath), version.Trim(), name.Trim(), target.Trim(), latest, output, cleanup);
}

sealed record Options
(
    DirectoryInfo CuoBinPath,
    string Version,
    string Name,
    string Target,
    bool IsLatest,
    string ManifestOutput,
    bool Cleanup
);

sealed class HashFile
{
    public HashFile(string fileName, string hash, string url, UpdateAction action = UpdateAction.None)
    {
        Filename = fileName;
        Hash = hash;
        Url = url;
        Action = action;
    }

    public string Filename { get; }
    public string Url { get; set; }
    public string Hash { get; }
    public UpdateAction Action { get; }

    public void Save(XmlWriter xml)
    {
        xml.WriteStartElement("file");
        xml.WriteAttributeString("filename", Filename.Replace('\\', '/'));

        if (Action == UpdateAction.Delete)
        {
            xml.WriteAttributeString("action", "del");
        }
        else
        {
            xml.WriteAttributeString("hash", Hash);
            xml.WriteAttributeString("url", Url);
        }

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

        Files = xml["files"].GetElementsByTagName("file").OfType<XmlElement>()
            .Select(s => new HashFile(
                s.GetAttribute("filename").Replace('\\', '/'),
                s.GetAttribute("hash"),
                s.GetAttribute("url"),
                s.GetAttribute("action") switch {
                    "del" => UpdateAction.Delete,
                    _ => UpdateAction.None,
                } ))
            .OrderBy(s => s.Filename)
            .ToList();
    }

    public void Save(XmlWriter xml)
    {
        xml.WriteStartElement("release");
        xml.WriteAttributeString("name", Name);
        xml.WriteAttributeString("version", Version);
        xml.WriteAttributeString("latest", IsLatest.ToString());

        xml.WriteStartElement("files");
        Files.ForEach(s => s.Save(xml));
        xml.WriteEndElement();

        xml.WriteEndElement();
    }
}

enum UpdateAction
{
    None,
    Update,
    Delete
}