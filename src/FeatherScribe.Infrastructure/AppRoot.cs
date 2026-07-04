using System.IO;
namespace FeatherScribe.Infrastructure;

/// <summary>
/// アプリのルートディレクトリ(config/appsettings.json を含む階層)を特定する。
/// 開発時は bin/Debug/... から実行されるため、上位ディレクトリを探索する。
/// </summary>
public static class AppRoot
{
    public static string Locate()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "config", "appsettings.json")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }
        }

        return AppContext.BaseDirectory;
    }
}
