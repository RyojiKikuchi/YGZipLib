# YGZipLib

YGZipLib is a compression/decompression library for ZIP archives.  

# Features

Compression/decompression processes are performed in multiprocessing  
AES encryption support  
Easy to use

# Usage

- Compression method

```c#
    using YGZipLib;

    public void Compress()
    {
        // // Instances should be Disposed.
         using(ZipArcClass zip = new ZipArcClass(@"R:\example.zip"))
         {
             // Compression Option Setting
             zip.EncryptionOption = ZipArcClass.ENCRYPTION_OPTION.AES256;
             zip.Password = "password";
             // single file compression
             zip.AddFilePath(@"R:\example_file.txt");
             // directory compression
             zip.AddDirectory(@"R:\example_dir");
             // Finish call
             zip.Finish();
         }
     }
```

- Decompression Method

```c#
    using YGZipLib;
    using System.Collections.Generic;
    using System.IO;

    public void Decompress()
    {
        // Instances should be Disposed.
        using(UnZipArcClass unzip = new UnZipArcClass(@"R:\example.zip"))
        {
            // Set password if the file is encrypted.
            unzip.Password = "password";

            // --- Processed per file ---
            // Obtaining a list of files in a ZIP archive
            List<UnZipArcClass.CentralDirectoryInfo> dirList = unzip.GetFileList;
            // Loop and process
            dirList.FindAll(d => d.IsDirectory == false).ForEach(d => unzip.PutFile(d, Path.Combine(@"R:\uncompressdir", d.FileName)));

            // --- batch directory processing ---
            unzip.ExtractAllFiles(new DirectoryInfo(@"R:\uncompressdir"));
        }
    }
```

- Output ZIP file as Stream by web service, etc.

```C#
    using YGZipLib;
    using System.IO;

    /// <summary>
    /// Example of sending a ZIP file via a web service, etc.
    /// </summary>
    /// <param name="resStream"></param>
    public async void SendZipFile (Stream resStream)
    {
        // Instances should be Disposed.
        using(ZipArcClass zip = new ZipArcClass(resStream))
        {
            await zip.AddFileStreamAsync("example1.dat", stream1);
            await zip.AddFileStreamAsync("/dir/example2.dat", stream2);
            await zip.FinishAsync();
        }
    }
```

- Async
```C#
    // Example
    List<Task> taskList = new List<Task>();
    taskList.Add(zip.AddFilePathAsync(@"D:\example1.txt"));
    taskList.Add(zip.AddFilePathAsync(@"D:\example2.txt"));
    taskList.Add(zip.AddFilePathAsync(@"D:\example3.txt"));
    Task t = Task.WhenAll(taskList);
```

- Other Settings

ZipArcClass
```C#
    // Processing multiplicity and working directory
    // ProcessorCount or 4, whichever is smaller. 0 is the default value.
    // For slow storage, process multiplexing should be lowered.
    // The working directory should be a fast device; if null is specified, it is automatically set from the environment.
    YGZipLib.ZipArcClass zip = new ZipArcClass(@"D:\zipfile.zip", 8, @"R:\temp");

    // Only archive without compression
    zip.CompressionOption = ZipArcClass.COMPRESSION_OPTION.NOT_COMPRESSED;

    // If you want the file to be uncompressed, specify the extension with a regular expression.
    zip.DontCompressExtRegExList.Add(new Regex("jpg", RegexOptions.IgnoreCase));
    zip.DontCompressExtRegExList.Add(new Regex("xls.*", RegexOptions.IgnoreCase));

    // Encryption Algorithms
    zip.EncryptionOption = ZipArcClass.ENCRYPTION_OPTION.TRADITIONAL;   // default
    zip.EncryptionOption = ZipArcClass.ENCRYPTION_OPTION.AES256;        // AES 256bit

    // Encoding of store file name and password.
    // The default is System.Globalization.CultureInfo.CurrentUICulture.TextInfo.ANSICodePage
    // See Note
    zip.ZipFileNameEncoding = System.Text.Encoding.GetEncoding("shift-jis");
```
__Note on ZipFileNameEncoding__  
For non- NET Framework environments (e.g., .net Core, .net5 or later), only ascii and utf are available unless CodePagesEncodingProvider is registered.  
Reference Site [https://learn.microsoft.com/ja-jp/dotnet/api/system.text.codepagesencodingprovider](https://learn.microsoft.com/ja-jp/dotnet/api/system.text.codepagesencodingprovider)  
If CodePagesEncodingProvider is not registered and the OS language does not support Encoding, the file name is stored in UTF-8.  
```C#
    // CodePagesEncodingProvider Registration
    // Register once at the start of the program
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
```


UnZipArcClass

```C#
    // Get list of stored files.
    List<YGZipLib.UnZipArcClass.CentralDirectoryInfo> dirList = unzip.GetFileList;

    // Processing multiplicity and working directory
    // See ZipArcClass
    YGZipLib.UnZipArcClass unzip = new UnZipArcClass(@"D:\zipfile.zip", 4, @"R:\temp");

    // TextEncodings of stored files.
    // See ZipArcClass
    unzip.ZipFileNameEncoding = System.Text.Encoding.GetEncoding("shift-jis");
```

# Note
Finish should be called at the end when creating a ZIP archive.

# License
 
"YGZipLib" is under [MIT license](https://opensource.org/licenses/mit-license.php)
