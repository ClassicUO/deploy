using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

using var md5 = MD5.Create();


var cuoBinPath = new DirectoryInfo(Path.GetFullPath(args[0]));
Console.WriteLine("CUOPATH: {0}", cuoBinPath);

var releaseVersion = args[1];
Console.WriteLine("VERSION: {0}", releaseVersion);

var releaseName = args[2];
Console.WriteLine("NAME: {0}", releaseName);

var manifestPath = args.Length >= 4 ? args[3] : "";
Console.WriteLine("OLD MANIFEST: {0}", manifestPath);


var releasesList = ReadManifest(manifestPath);
if (releasesList.Any(s => s.Version.Equals(releaseVersion)))
{
    Console.WriteLine("a release with version '{0}' already exists!", releaseVersion);
    return;
}

var deployFolder = new DirectoryInfo(Path.GetDirectoryName(manifestPath));
var osTarget = manifestPath.Replace("_manifest.xml", "");

var currentRelease = CreateReleaseFromFolder(cuoBinPath, releaseVersion, releaseName, true);

releasesList.ForEach(s => s.IsLatest = false);
releasesList.Add(currentRelease);

UpdateDiffFolders(currentRelease, deployFolder, osTarget, cuoBinPath);
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