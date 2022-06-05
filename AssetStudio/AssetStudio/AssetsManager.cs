using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssetStudio
{
    public class AssetsManager
    {
        public string SpecifyUnityVersion;
        public List<SerializedFile> assetsFileList = new List<SerializedFile>();
        private List<string> importFiles = new List<string>();

        internal Dictionary<string, int> assetsFileIndexCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private HashSet<string> assetsFileListHash
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private HashSet<string> importFilesHash = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal Dictionary<string, BinaryReader> resourceFileReaders
            = new Dictionary<string, BinaryReader>(StringComparer.OrdinalIgnoreCase);

        public void LoadFile(string dummyPath, Stream stream)
        {
            var reader = new FileReader(dummyPath, stream);
            LoadFile(reader);

            importFiles.Clear();
            importFilesHash.Clear();
            assetsFileListHash.Clear();

            ReadAssets();
            // ProcessAssets();
            // Level 1
        }

        public void LoadFile(string fullName)
        {
            var reader = new FileReader(fullName);
            LoadFile(reader);
        }

        public void LoadFile(FileReader reader)
        {
            switch (reader.FileType)
            {
#if ORG
                case FileType.AssetsFile:
                    LoadAssetsFile(reader);
                    break;
#endif
                case FileType.BundleFile:
                    LoadBundleFile(reader);
                    break;
                case FileType.WebFile:
                    LoadWebFile(reader);
                    break;
#if ORG
                case FileType.GZipFile:
                    LoadFile(DecompressGZip(reader));
                    break;
                case FileType.BrotliFile:
                    LoadFile(DecompressBrotli(reader));
                    break;
#endif
            }
        }

#if ORG
        private void LoadAssetsFile(FileReader reader)
        {
            if (!assetsFileListHash.Contains(reader.FileName))
            {
                try
                {
                    var assetsFile = new SerializedFile(reader, this);
                    CheckStrippedVersion(assetsFile);
                    assetsFileList.Add(assetsFile);
                    assetsFileListHash.Add(assetsFile.fileName);

                    foreach (var sharedFile in assetsFile.m_Externals)
                    {
                        var sharedFileName = sharedFile.fileName;

                        if (!importFilesHash.Contains(sharedFileName))
                        {
                            var sharedFilePath = Path.Combine(Path.GetDirectoryName(reader.FullPath), sharedFileName);
                            if (!File.Exists(sharedFilePath))
                            {
                                var findFiles = Directory.GetFiles(Path.GetDirectoryName(reader.FullPath), sharedFileName, SearchOption.AllDirectories);
                                if (findFiles.Length > 0)
                                {
                                    sharedFilePath = findFiles[0];
                                }
                            }

                            if (File.Exists(sharedFilePath))
                            {
                                importFiles.Add(sharedFilePath);
                                importFilesHash.Add(sharedFileName);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    reader.Dispose();
                    throw new Exception($"Error while reading assets file {reader.FileName}", e);
                }
            }
            else
            {
                reader.Dispose();
            }
        }
#endif
        private void LoadBundleFile(FileReader reader, string originalPath = null)
        {
            try
            {
                var bundleFile = new BundleFile(reader);
                foreach (var file in bundleFile.fileList)
                {
                    var dummyPath = Path.Combine(Path.GetDirectoryName(reader.FullPath), file.fileName);
                    var subReader = new FileReader(dummyPath, file.stream);
                    if (subReader.FileType == FileType.AssetsFile)
                    {
                        LoadAssetsFromMemory(subReader, originalPath ?? reader.FullPath, bundleFile.m_Header.unityRevision);
                    }
                    else
                    {
                        resourceFileReaders[file.fileName] = subReader; //TODO
                    }
                }
            }
            catch (Exception e)
            {
                var str = $"Error while reading bundle file {reader.FileName}";
                if (originalPath != null)
                {
                    str += $" from {Path.GetFileName(originalPath)}";
                }
                throw new Exception(str, e);
            }
            finally
            {
                reader.Dispose();
            }
        }

        private void LoadAssetsFromMemory(FileReader reader, string originalPath, string unityVersion = null)
        {
            // reader = subreader, dummyPath
            if (!assetsFileListHash.Contains(reader.FileName))
            {
                try
                {
                    var assetsFile = new SerializedFile(reader, this);
                    assetsFile.originalPath = originalPath;
                    if (!string.IsNullOrEmpty(unityVersion) && assetsFile.header.m_Version < SerializedFileFormatVersion.kUnknown_7)
                    {
                        assetsFile.SetVersion(unityVersion);
                    }
                    CheckStrippedVersion(assetsFile);
                    assetsFileList.Add(assetsFile);
                    assetsFileListHash.Add(assetsFile.fileName);
                }
                catch (Exception e)
                {
                    resourceFileReaders.Add(reader.FileName, reader);
                    throw new Exception($"Error while reading assets file {reader.FileName} from {Path.GetFileName(originalPath)}", e);
                }
            }
        }

        private void LoadWebFile(FileReader reader)
        {
            // Logger.Info("Loading " + reader.FileName);
            try
            {
                var webFile = new WebFile(reader);
                foreach (var file in webFile.fileList)
                {
                    var dummyPath = Path.Combine(Path.GetDirectoryName(reader.FullPath), file.fileName);
                    var subReader = new FileReader(dummyPath, file.stream);
                    switch (subReader.FileType)
                    {
                        case FileType.AssetsFile:
                            LoadAssetsFromMemory(subReader, reader.FullPath);
                            break;
                        case FileType.BundleFile:
                            LoadBundleFile(subReader, reader.FullPath);
                            break;
                        case FileType.WebFile:
                            LoadWebFile(subReader);
                            break;
                        case FileType.ResourceFile:
                            resourceFileReaders[file.fileName] = subReader; //TODO
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                throw new Exception($"Error while reading web file {reader.FileName}", e);
            }
            finally
            {
                reader.Dispose();
            }
        }

        public void CheckStrippedVersion(SerializedFile assetsFile)
        {
            if (assetsFile.IsVersionStripped && string.IsNullOrEmpty(SpecifyUnityVersion))
            {
                throw new Exception("The Unity version has been stripped, please set the version in the options");
            }
            if (!string.IsNullOrEmpty(SpecifyUnityVersion))
            {
                assetsFile.SetVersion(SpecifyUnityVersion);
            }
        }

        private void ReadAssets()
        {
            var progressCount = assetsFileList.Sum(x => x.m_Objects.Count);
            foreach (var assetsFile in assetsFileList)
            {
                foreach (var objectInfo in assetsFile.m_Objects)
                { // subreader
                    var objectReader = new ObjectReader(assetsFile.reader, assetsFile, objectInfo);
                    try
                    {
                        Object obj;
                        switch (objectReader.type)
                        {
#if ORG
                            case ClassIDType.Animation:
                                obj = new Animation(objectReader);
                                break;
                            case ClassIDType.AnimationClip:
                                obj = new AnimationClip(objectReader);
                                break;
                            case ClassIDType.Animator:
                                obj = new Animator(objectReader);
                                break;
                            case ClassIDType.AnimatorController:
                                obj = new AnimatorController(objectReader);
                                break;
                            case ClassIDType.AnimatorOverrideController:
                                obj = new AnimatorOverrideController(objectReader);
                                break;
#endif
                            case ClassIDType.AssetBundle:
                                obj = new AssetBundle(objectReader);
                                break;
#if ORG
                            case ClassIDType.AudioClip:
                                obj = new AudioClip(objectReader);
                                break;
                            case ClassIDType.Avatar:
                                obj = new Avatar(objectReader);
                                break;
                            case ClassIDType.Font:
                                obj = new Font(objectReader);
                                break;
                            case ClassIDType.GameObject:
                                obj = new GameObject(objectReader);
                                break;
                            case ClassIDType.Material:
                                obj = new Material(objectReader);
                                break;
                            case ClassIDType.Mesh:
                                obj = new Mesh(objectReader);
                                break;
                            case ClassIDType.MeshFilter:
                                obj = new MeshFilter(objectReader);
                                break;
                            case ClassIDType.MeshRenderer:
                                obj = new MeshRenderer(objectReader);
                                break;
                            case ClassIDType.MonoBehaviour:
                                obj = new MonoBehaviour(objectReader);
                                break;
                            case ClassIDType.MonoScript:
                                obj = new MonoScript(objectReader);
                                break;
                            case ClassIDType.MovieTexture:
                                obj = new MovieTexture(objectReader);
                                break;
                            case ClassIDType.PlayerSettings:
                                obj = new PlayerSettings(objectReader);
                                break;
                            case ClassIDType.RectTransform:
                                obj = new RectTransform(objectReader);
                                break;
                            case ClassIDType.Shader:
                                obj = new Shader(objectReader);
                                break;
                            case ClassIDType.SkinnedMeshRenderer:
                                obj = new SkinnedMeshRenderer(objectReader);
                                break;
                            case ClassIDType.Sprite:
                                obj = new Sprite(objectReader);
                                break;
                            case ClassIDType.SpriteAtlas:
                                obj = new SpriteAtlas(objectReader);
                                break;
                            case ClassIDType.TextAsset:
                                obj = new TextAsset(objectReader);
                                break;
#endif
                            case ClassIDType.Texture2D:
                                obj = new Texture2D(objectReader);
                                break;
#if ORG
                            case ClassIDType.Transform:
                                obj = new Transform(objectReader);
                                break;
                            case ClassIDType.VideoClip:
                                obj = new VideoClip(objectReader);
                                break;
                            case ClassIDType.ResourceManager:
                                obj = new ResourceManager(objectReader);
                                break;
#endif
                            default:
                                obj = new Object(objectReader);
                                break;
                        }
                        assetsFile.AddObject(obj);
                    }
                    catch (Exception e)
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("Unable to load object")
                            .AppendLine($"Assets {assetsFile.fileName}")
                            .AppendLine($"Type {objectReader.type}")
                            .AppendLine($"PathID {objectInfo.m_PathID}")
                            .Append(e);
                        throw new Exception(sb.ToString());
                    }
                }
            }
        }

        public void Clear()
        {
            foreach (var assetsFile in assetsFileList)
            {
                assetsFile.Objects.Clear();
                assetsFile.reader.Close();
            }
            assetsFileList.Clear();
            foreach (var resourceFileReader in resourceFileReaders)
            {
                resourceFileReader.Value.Close();
            }
            resourceFileReaders.Clear();
            assetsFileIndexCache.Clear();
            assetsFileListHash.Clear();
            importFilesHash.Clear();
        }
    }
}
