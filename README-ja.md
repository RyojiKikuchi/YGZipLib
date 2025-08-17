# YGZipLib

YGZipLibはZIPファイルの作成、展開が簡単に行えるクラスライブラリです

# Features

- マルチプロセス
- AES暗号化
- Easy to use

# Usage

- 書庫作成

```c#
    using YGZipLib;

    public void Compress()
    {
        // 最後にDisposeを呼び出すこと
         using(ZipArcClass zip = new ZipArcClass(@"R:\example.zip"))
         {
             // 暗号化設定
             zip.EncryptionOption = ZipArcClass.ENCRYPTION_OPTION.AES256;
             zip.Password = "password";
             // 1ファイル格納
             zip.AddFilePath(@"R:\example_file.txt");
             // ディレクトリ格納
             zip.AddDirectory(@"R:\example_dir");
             // ZIPファイル格納終了
             zip.Finish();
         }
     }
```

- 展開

```c#
    using YGZipLib;
    using System.Collections.Generic;
    using System.IO;

    public void Decompress()
    {
        // 最後にDisposeを呼び出すこと
        using(UnZipArcClass unzip = new UnZipArcClass(@"R:\example.zip"))
        {
            // 暗号化ZIPの場合はパスワードを指定する
            unzip.Password = "password";

            // --- 1ファイル毎に展開 ---
            // 1.GetFileListで書庫内のファイルリストを取得
            List<UnZipArcClass.CentralDirectoryInfo> dirList = unzip.GetFileList;
            // 2.PutFileに展開するファイルを指定して取り出し
            dirList.FindAll(d => d.IsDirectory == false).ForEach(d => unzip.PutFile(d, Path.Combine(@"R:\uncompressdir", d.FileName)));

            // --- 全て展開 ---
            // ExtractAllFilesに出力先ディレクトリを指定して全ファイル出力
            unzip.ExtractAllFiles(new DirectoryInfo(@"R:\uncompressdir"));
        }
    }
```

- ZIPファイルをStreamに出力

```C#
    using YGZipLib;
    using System.IO;

    /// <summary>
    /// Example of sending a ZIP file via a web service, etc.
    /// </summary>
    /// <param name="resStream"></param>
    public async void SendZipFile (Stream resStream)
    {
        using(ZipArcClass zip = new ZipArcClass(resStream))
        {
            await zip.AddFileStreamAsync("example1.dat", stream1);
            await zip.AddFileStreamAsync("/dir/example2.dat", stream2);
            await zip.FinishAsync();
        }
    }
```

- async/multiprocess
```C#
    // 非同期メソッド呼び出し例
    await zip.AddFilePathAsync(@"D:\example1.txt");
    await zip.AddFilePathAsync(@"D:\example2.txt");
    await zip.AddFilePathAsync(@"D:\example3.txt");

    // 多重処理例
    List<Task> taskList = new List<Task>();
    taskList.Add(zip.AddFilePathAsync(@"D:\example1.txt"));
    taskList.Add(zip.AddFilePathAsync(@"D:\example2.txt"));
    taskList.Add(zip.AddFilePathAsync(@"D:\example3.txt"));
    Task t = Task.WhenAll(taskList);

    // AddDirectory/AddDirectoryAsyncは内部で多重処理が行われる
    zip.AddDirectory(@"D:\Compdir");

    // 非同期処理時はFinish/FinishAsyncはAdd系の処理が完了してから呼び出す
    await zip.FinishAsync();

    // 非同期処理のキャンセル例
    CancellationTokenSource tokenSource = new CancellationTokenSource();
    CancellationToken token = tokenSource.Token;
    await zip.AddDirectoryAsync(@"D:\Compdir", null, token);
    // キャンセル
    tokenSource.Cancel();
```

- Other Settings / ZipArcClass
```C#
    // 多重度とテンポラリファイル作成ディレクトリの指定
    // 多重度の初期値は論理プロセッサ数と4の小さい方。0を指定した場合はデフォルト値
    // 作業ディレクトリは未指定時は自動取得。
    YGZipLib.ZipArcClass zip = new ZipArcClass(@"D:\zipfile.zip", 8, @"R:\temp");

    // 圧縮オプションの設定
    zip.CompressionOption = ZipArcClass.COMPRESSION_OPTION.NOT_COMPRESSED;

    // 圧縮対象外とする拡張子のリストを正規表現で指定する
    zip.DontCompressExtRegExList.Add(new Regex("jpg", RegexOptions.IgnoreCase));
    zip.DontCompressExtRegExList.Add(new Regex("xls.*", RegexOptions.IgnoreCase));

    // 暗号化アルゴリズム
    zip.EncryptionOption = ZipArcClass.ENCRYPTION_OPTION.TRADITIONAL;   // default
    zip.EncryptionOption = ZipArcClass.ENCRYPTION_OPTION.AES256;        // AES 256bit

    // 格納ファイル名とパスワードのEncoding  初期値は System.Globalization.CultureInfo.CurrentUICulture.TextInfo.ANSICodePage
    // 注意参照
    zip.ZipFileNameEncoding = System.Text.Encoding.GetEncoding("shift-jis");
```
__ZipFileNameEncodingの注意__  
.NET Framework環境以外(例:.net Core, .net5以降)では CodePagesEncodingProvider を登録しないとasciiとutf系しか利用できない。  
参考サイト [https://learn.microsoft.com/ja-jp/dotnet/api/system.text.codepagesencodingprovider](https://learn.microsoft.com/ja-jp/dotnet/api/system.text.codepagesencodingprovider)  
CodePagesEncodingProvider未登録でOSの言語が対応していないEncodingの場合、ファイル名は UTF-8 で格納される。  
```C#
    // CodePagesEncodingProvider登録
    // プログラム開始時に一度登録する
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
```

- Other Settings / UnZipArcClass
```C#
    // zipファイルへの格納ファイル一覧取得
    List<YGZipLib.UnZipArcClass.CentralDirectoryInfo> dirList = unzip.GetFileList;

    // 多重度とテンポラリファイル作成ディレクトリの指定
    // 多重度の初期値は論理プロセッサ数。0を指定した場合はデフォルト値
    // 作業ディレクトリは未指定時は自動取得。
    YGZipLib.UnZipArcClass unzip = new UnZipArcClass(@"D:\zipfile.zip", 4, @"R:\temp");

    // 格納ファイル名のEncoding  初期値は System.Text.Encoding.Default.CodePage.
    // ZipArcClassの注意参照
    unzip.ZipFileNameEncoding = System.Text.Encoding.GetEncoding("shift-jis");
```

# Note
Finish should be called at the end when creating a ZIP archive.

# License
 
"YGZipLib" is under [MIT license](https://opensource.org/licenses/mit-license.php)

This library includes the work that is distributed in the [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0).  
The Deflate64 code included in YGZipLib is based on the following java source from [Apache Commons Compress™](https://commons.apache.org/proper/commons-compress/).

