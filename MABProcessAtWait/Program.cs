﻿using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows.Forms;

/*
 フォルダ構成
MinecraftAutoBackup.exe
Configs
    config.txt
    BackupPath.txt
SubModules
    MABProcess.exe
image
    app.ico
 */

namespace MABProcessAtWait {
    static class Program {
        /// <summary>
        /// アプリケーションのメイン エントリ ポイントです。
        /// </summary>
        [STAThread]
        static void Main() {

            // © DOBON!.
            //Mutex関係
            // https://dobon.net/vb/dotnet/process/checkprevinstance.html
            //Mutex名を決める（必ずアプリケーション固有の文字列に変更すること！）
            string mutexName = "MABProcess";
            //Mutexオブジェクトを作成する
            System.Threading.Mutex mutex = new System.Threading.Mutex(false, mutexName);

            bool hasHandle = false;
            try {
                try {
                    hasHandle = mutex.WaitOne(0, false);
                }
                //.NET Framework 2.0以降の場合
                catch (System.Threading.AbandonedMutexException) {
                    hasHandle = true;
                }
                if (hasHandle == false) {
                    return;
                }

                    new AppConfig();
                    Config.Load();
                    Util.NotReadonly(AppConfig.BackupPath);
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Form1 f = new Form1();
                    Application.Run();

            }
            finally {
                if (hasHandle) {
                    mutex.ReleaseMutex();
                }
                mutex.Close();
            }

        }
    }

    public partial class Form1 :Form {
        System.Windows.Forms.Timer timer;
        string backupDataPath;
        NotifyIcon notifyIcon;
        bool isRunning = false;

        public Form1() {
            backupDataPath = AppConfig.BackupPath;
            this.ShowInTaskbar = false;
            this.Icon = new Icon(".\\Image\\app.ico");
            this.FormClosing += new FormClosingEventHandler(Form1_Closing);

            timer = new System.Windows.Forms.Timer() {
                Enabled = true
            };
            timer.Interval = 1000;
            timer.Tick += new EventHandler(timer_Tick);

            notifyIcon = new NotifyIcon();
            notifyIcon.Icon = new Icon(".\\Image\\app_sub.ico");
            notifyIcon.Visible = true;
            notifyIcon.Text = "MAB待機モジュール";
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem exit = new ToolStripMenuItem();
            exit.Text = "終了";
            exit.Click += new EventHandler(Close_Click);
            menu.Items.Add(exit);
            notifyIcon.ContextMenuStrip = menu;
        }

        void Close_Click(object sender, EventArgs e) {
            Logger.Info("アプリケーションが終了しました");
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            Application.Exit();
        }

        void Form1_Closing(object sender, EventArgs e) {
            Logger.Info("アプリケーションが強制終了しました");
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
        }

        void timer_Tick(object sender, EventArgs e) {
            // lancherが起動してない場合 => 何もしない
            // lancherが起動していてflagがfalse => flagをtrueにしてバックアップ動作
            // lancherが起動していてflagがtrue => 何もしない
            // lancherが起動していなくてflagがtrue => flagをfalseにする
            if (Process.GetProcessesByName("MinecraftLauncher").Length > 0 && !isRunning) {
                while (Util.IsZipperRunning()) {
                    System.Threading.Tasks.Task.Delay(10000);
                }
                isRunning = true;
                Logger.Info("Minecraft Lancherの起動を検知しました");
                Logger.Info("isRunningがfalseに設定されていました");
                Logger.Info("バックアッププロセスを始めます");
                notifyIcon.Icon = new Icon(".\\Image\\app_sub_doing.ico");
                int backupCount = 0;
                ContextMenuStrip menu = new ContextMenuStrip();
                ToolStripMenuItem exit = new ToolStripMenuItem() {
                    Text = "強制終了",
                };
                exit.Click += new EventHandler(Close_Click);
                menu.Items.Add(exit);
                notifyIcon.ContextMenuStrip = menu;

                List<string> worldPasses = GetWorldPasses();// バックアップをするワールドへのパス一覧
                string nowTime = DateTime.Now.ToString("yyyyMMddHHmm");
                if (worldPasses.Count == 0) {
                    Logger.Info("どうやらバックアップ予定のデータはないようです");
                }

                notifyIcon.Text = $"{backupCount}/{worldPasses.Count}";

                foreach (string worldPath in worldPasses) {
                    notifyIcon.Text = $"{backupCount++}/{worldPasses.Count}";
                    //前回のリロードとバックアップまでの間にワールドが消された場合
                    string backupPath = backupDataPath + "\\" + Path.GetFileName(Directory.GetParent(Directory.GetParent(worldPath).ToString()).ToString()) + "\\" + Path.GetFileName(worldPath) + "\\" + nowTime;
                    string worldBackupPath = backupDataPath + "\\" + Path.GetFileName(Directory.GetParent(Directory.GetParent(worldPath).ToString()).ToString()) + "\\" + Path.GetFileName(worldPath);
                    try { doBackup(worldPath, nowTime); }
                    catch (DirectoryNotFoundException dnfe) {

                        Console.Error.WriteLine(worldPath + ":DirectoryNotFoundException : " + dnfe.Message);
                        if (!Directory.Exists(worldPath)) {
                            DialogResult r = MessageBox.Show(
                            $"バックアップ予定のワールドデータ[{Path.GetFileName(worldPath)}]が見つかりませんでした。",
                            "Minecraft Auto Backup",
                            MessageBoxButtons.OK);
                        }
                    }

                    //バックアップ超過分削除
                    if (AppConfig.BackupCount != "無制限") {
                        if (Directory.GetFileSystemEntries(worldBackupPath).ToList().Count() > int.Parse(AppConfig.BackupCount)) {
                            Logger.Info($"{worldBackupPath}のバックアップ数({Directory.GetFileSystemEntries(worldBackupPath).ToList().Count()})が超過している(AppConfig:{int.Parse(AppConfig.BackupCount)})ので削除処理に移ります");
                            //バックアップ数がappconfig.backupCountより多い場合超過分を削除する
                            List<string> backups = Directory.GetFileSystemEntries(worldBackupPath)
                                .OrderByDescending(filePath => File.GetLastWriteTime(filePath).Date)
                                .ThenByDescending(filePath => File.GetLastWriteTime(filePath).TimeOfDay).ToList();
                            Logger.Debug($"buckups count: {backups.Count()}");
                            foreach (string s in backups) {
                                Logger.Debug($"backups:[{s}]");
                            }
                            List<string> deleteBackups = new List<string>();
                            for (int i = int.Parse(AppConfig.BackupCount); i < backups.Count(); i++) {
                                Logger.Info($"{backups[i]}を削除します");

                                //zipファイルかどうか判定
                                if (backups[i].Contains(".zip")) {
                                    //zipファイルの場合
                                    try { File.Delete(backups[i]); }
                                    catch (Exception exc) {
                                        Logger.Error($"{backups[i]}");
                                        Logger.Error($"{exc.Message}");
                                        Logger.Error($"{exc.StackTrace}");
                                    }
                                }
                                else {
                                    //ディレクトリの場合
                                    try { Directory.Delete(backups[i], true); }
                                    catch (Exception exc) {
                                        Logger.Error($"{backups[i]}");
                                        Logger.Error($"{exc.Message}");
                                        Logger.Error($"{exc.StackTrace}");
                                    }
                                }

                            }
                        }
                        else {
                            Logger.Info($"{worldBackupPath}({Directory.GetFileSystemEntries(worldBackupPath).ToList().Count()})の超過分(AppConfig:{int.Parse(AppConfig.BackupCount)})は発見されませんでした");
                        }
                    }
                    //バックアップ保持数を調整

                }
                Config.ReloadConfig();
                Logger.Info("全バックアップが完了しました ");

                timer.Enabled = true;
                notifyIcon.Icon = new Icon(".\\Image\\app_sub.ico");
                notifyIcon.Text = "MAB待機モジュール";
            }
            else if (!(Process.GetProcessesByName("MinecraftLauncher").Length > 0) && isRunning) {
                Logger.Info("ランチャーの停止を検知しました");
                Logger.Info("isRunningにfalseを設定します");
                isRunning = false;
            }
        }

        //バックアップをするワールドデータのパスを配列にして返す
        List<string> GetWorldPasses() {
            List<World> _worldPasses = new List<World>();
            List<string> worldPasses = new List<string>();
            _worldPasses = Config.GetConfig();
            foreach (World w in _worldPasses) {
                if (w.WDoBackup && w.isAlive) {
                    //バックアップをする予定でかつ、バックアップ元が生きているワールドのみ追加
                    worldPasses.Add(w.WPath);
                }
            }
            return worldPasses;
        }

        void doBackup(string path, string Time) {
            string backupPath = backupDataPath + "\\" + Path.GetFileName(Directory.GetParent(Directory.GetParent(path).ToString()).ToString()) + "\\" + Path.GetFileName(path) + "\\" + Time;
            string worldBackupPath = backupDataPath + "\\" + Path.GetFileName(Directory.GetParent(Directory.GetParent(path).ToString()).ToString()) + "\\" + Path.GetFileName(path);
            if (AppConfig.DoZip)
                backupPath += ".zip";
            if (!Directory.Exists(worldBackupPath)) {
                Directory.CreateDirectory(worldBackupPath);
            }
            if (AppConfig.DoZip) {
                Logger.Info(path + " を " + backupPath + " へバックアップ中です");
                ZipFile.CreateFromDirectory(path, backupPath);
                Logger.Info(path + " を " + backupPath + "へバックアップしました");
            }
            else {
                Logger.Info(path + " を " + backupPath + "へバックアップ中です");
                FileSystem.CopyDirectory(path, backupPath);
                Logger.Info(path + " を " + backupPath + "へバックアップしました");
            }



        }
    }

    public class BackupTimes {
        public string worldPath;
        public DateTime nextBackupTime;
        public BackupTimes(string path, string time) {
            worldPath = path;
            nextBackupTime = DateTimeOffset.FromUnixTimeSeconds(long.Parse(time)).DateTime;
        }
    }

    public static class Util {
        public static bool IsZipperRunning() {
            //一回もzipperが起動されていない場合
            if (!File.Exists($".\\logs\\Zipper.txt")) {
                return false;
            }
            //zipperのlogからlog取得
            List<string> strs = new List<string>();
            using (StreamReader r = new StreamReader($".\\logs\\Zipper.txt")) {
                while (r.Peek() > -1) {
                    strs.Add(r.ReadLine());
                }
            }
            //最終行がExit Processかどうかを取得
            string decisionStr = strs[strs.Count() - 2].Substring(28, strs[strs.Count() - 2].Length);
            Logger.Info($"decisionStrは{decisionStr}です");
            return decisionStr != "Exit Process";
        }

        public static string TrimDoubleQuotationMarks(string target) {
            return target.Trim(new char[] { '"' });
        }

        public static void NotReadonly(string path) {
            Logger.Debug("call:NotReadonly");
            Logger.Info($"{path}を入力されました");
            List<string> pasess = Directory.GetFiles(path, "*", System.IO.SearchOption.AllDirectories).ToList();
            foreach(string p in pasess) {
                if((File.GetAttributes(p) & FileAttributes.ReadOnly) == FileAttributes.ReadOnly) {
                    Logger.Info($"{p} のreadonlyを外します");
                    File.SetAttributes(p, File.GetAttributes(p) & ~FileAttributes.ReadOnly);
                }
            }
        }
    }

    public class Config {
        /*
         必要な関数
        与えられたワールドオブジェクトをコンフィグファイルに書き変える
        コンフィグファイルの中身を渡す関数
        コンフィグファイルがないときにコンフィグファイルを作る関数
        コンフィグファイルからメモリに読み込む関数
        メモリの内容をコンフィグファイルに書き込む関数
        コンフィグファイルの内容をハードディスクの内容と照らし合わせて更新する
         ハードディスクの内容をワールドオブジェクトのListにして返す
        与えられたワールドオブジェクトをコンフィグファイルに書き加える
        与えられたワールドオブジェクトをコンフィグファイルから消す

        必要な関数:改良案

         コンフィグファイルがないときにコンフィグファイルを作る関数
         コンフィグファイルからメモリに読み込む関数
         メモリの内容をコンフィグファイルに書き込む関数
        コンフィグファイルの内容をハードディスクの内容と照らし合わせて更新する
         ハードディスクの内容をワールドオブジェクトのListにして返す
        与えられたワールドオブジェクトをメモリに書き変える
         */
        /*
        バックアップに関するオプションを記録するtxtファイル
        "バックアップの可否","ワールド名","ワールドへのパス","ワールドの所属するディレクトリ"
        が入っている
        */
        public static List<World> configs = new List<World>();

        public static string configPath = @".\Config\config.txt";

        //datasの中にworldName,worldDirに当てはまる要素があるかどうか
        private static bool IsWorldParticular(string worldName, string worldDir, string[] datas) {
            //Logger.Info(datas[1] + ",\"" + worldName + "\"と" + datas[3] + ",\"" + worldDir + "\"");
            return datas[1] == "\"" + worldName + "\"" && datas[3] == "\"" + worldDir + "\"";
        }

        public static List<World> GetConfig() => configs;

        /// <summary>
        /// ConfigファイルからAppに読み込む
        /// </summary>
        public static void Load() {
            Logger.Debug("call:LoadConfigToApp");
            List<string> texts = new List<string>();
            using (StreamReader reader = new StreamReader(configPath, Encoding.GetEncoding("utf-8"))) {
                while (reader.Peek() >= 0) {
                    List<string> datas = reader.ReadLine().Split(',').ToList();
                    datas = datas.Select(x => Util.TrimDoubleQuotationMarks(x)).ToList();
                    configs.Add(new World(datas[2], Convert.ToBoolean(datas[0]), Convert.ToBoolean(datas[4])));
                }
                Logger.Info($"Configから{configs.Count()}件のワールドを読み込みました");
            }

        }

        /// <summary>
        /// configsをConfig.txtに上書きする
        /// </summary>
        public static void Write() {
            List<string> text = new List<string>();
            foreach (World config in configs) {
                text.Add($"\"{config.WDoBackup}\",\"{config.WName}\",\"{config.WPath}\",\"{config.WDir}\",\"{config.isAlive}\"\n");
            }
            File.WriteAllText(configPath, string.Join("", text), Encoding.GetEncoding("utf-8"));
        }


        /// <summary>
        /// Configファイルを更新する
        /// </summary>
        public static List<World> ReloadConfig() {
            Logger.Debug("call:reloadConfig");
            List<World> worldInHdd = GetWorldDataFromHDD();
            List<World> worldInConfig = GetConfig();
            Logger.Debug($"config: {worldInConfig.Count()}");
            Logger.Debug($"HDD   : {worldInHdd.Count()}");

            int i = 0;
            //configに存在しないpathをconfigに追加する
            foreach (World pc in worldInHdd) {
                Logger.Debug($"pc:{i}回目");
                //dobackup以外を比較して判定
                //List<WorldForComparison> _comp = worldInConfig.Select(x => new WorldForComparison(x)).ToList();
                if (!worldInConfig.Select(x => $"{x.WPath}_{x.isAlive}").ToList().Contains($"{pc.WPath}_{pc.isAlive}")) {
                    Logger.Info($"ADD {pc.WName}");
                    configs.Add(pc);
                }
                i++;
            }
            List<World> removeWorlds = new List<World>();
            Logger.Debug($"config: {worldInConfig.Count()}");
            Logger.Debug($"HDD   : {worldInHdd.Count()}");

            i = 0;
            //configに存在するがhddに存在しない(削除されたワールド)pathをconfigで死亡扱いにする
            //isAliveプロパティを追加したので、そちらで管理
            int wI = 0;
            //Logger.Info("-----config一覧-----");
            //foreach(var a in worldInHdd.Select(x => new WorldForComparison(x)).ToList()) {
            //    Logger.Info($"pc : {a.path}/{a.isAlive.ToString()}");
            //}
            //Logger.Info("--------------------");
            foreach (World world in worldInConfig) {
                Logger.Debug($"config:{i}回目");
                //dobackup以外を比較して判定
                if (!worldInHdd.Select(x => $"{x.WPath}_{x.isAlive}").ToList().Contains($"{world.WPath}_{world.isAlive}")) {
                    //config内のworldがHDDになかった場合
                    if (GetBackups(world).Count() == 0) {
                        // バックアップが一つもない場合はconfigから削除
                        Logger.Info($"バックアップが一つもないのでRemoveWorldsに{world.WName}を追加");
                        removeWorlds.Add(world);
                    }
                    else {
                        if (world.isAlive) {
                            //バックアップが一つでもある場合は、backup一覧に表示するために殺すだけにする
                            Logger.Info($"{world.WName}のバックアップが残っているため殺害");
                            Config.configs[wI].isAlive = false;
                            int count = 1;
                            while (Directory.Exists($"{AppConfig.BackupPath}\\{Config.configs[wI].WDir}\\{Config.configs[wI].WName}_(削除済み)_{count}")) {
                                Logger.Info($" path[ {AppConfig.BackupPath}\\{Config.configs[wI].WDir}\\{Config.configs[wI].WName}_(削除済み)_{count} ]");
                                count++;
                            }

                            Directory.Move($"{AppConfig.BackupPath}\\{ Config.configs[wI].WDir}\\{ Config.configs[wI].WName}",
                                $"{AppConfig.BackupPath}\\{ Config.configs[wI].WDir}\\{ Config.configs[wI].WName}_(削除済み)_{count}");
                            Config.configs[wI].WPath += "_(削除済み)_" + count;
                            Config.configs[wI].WName += "_(削除済み)_" + count;
                        }
                    }
                }
                wI++;
                i++;
            }

            Logger.Debug($"config: {worldInConfig.Count()}");
            Logger.Debug($"HDD   : {worldInHdd.Count()}");

            foreach (World w in removeWorlds) {
                if (configs.Remove(w)) {
                    Logger.Info($"REMOVE {w.WName} suc");
                }
                else {
                    Logger.Info($"REMOVE {w.WName} 見つかりませんでした");
                }
            }

            Write();

            Logger.Debug($"config: {worldInConfig.Count()}");
            Logger.Debug($"HDD   : {worldInHdd.Count()}");

            return removeWorlds;
        }
        private static List<string> GetBackups(World w) {
            try {
                return Directory.GetDirectories(AppConfig.BackupPath + "\\" + w.WDir + "\\" + w.WName).ToList();
            }
            catch (DirectoryNotFoundException) {
                Logger.Info($"{AppConfig.BackupPath}\\{w.WDir}\\{w.WName} にアクセスできませんでした");
                return new List<string>();
            }
        }

        public static void Change(string worldName, string worldDir, string doBackup) {
            Logger.Debug("call:Change");
            Logger.Debug("GET  worldName: " + worldName + ",  worldDir: " + worldDir + ",  dobackup: " + doBackup);
            List<World> _configs = new List<World>();
            foreach (World config in configs) {
                if (config.WName == worldName && config.WDir == worldDir) {
                    config.WDoBackup = bool.Parse(doBackup);
                    _configs.Add(new World(config.WPath, Convert.ToBoolean(doBackup), config.isAlive));
                }
                else {
                    _configs.Add(new World(config.WPath, config.WDoBackup, config.isAlive));
                }
            }
            configs = _configs;
            //ConsoleConfig();
        }

        /// <summary>
        /// PCからワールドデータ一覧を取得
        /// </summary>
        /// <returns>取得したList<world></returns>
        private static List<World> GetWorldDataFromHDD() {
            Logger.Debug("call:GetWorldDataFromPC");
            List<World> worlds = new List<World>();
            List<string> _gameDirectory = Directory.GetDirectories(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)).ToList();
            List<string> gameDirectory = new List<string>();
            foreach (string dir in _gameDirectory) {
                List<string> dirsInDir = Directory.GetDirectories(dir).ToList();
                dirsInDir = dirsInDir.Select(x => Path.GetFileName(x)).Cast<string>().ToList();
                if (dirsInDir.Contains("logs") && dirsInDir.Contains("resourcepacks") && dirsInDir.Contains("saves")) {
                    //Logger.Info($"ゲームディレクトリ[{dir}]を発見しました");
                    gameDirectory.Add(dir);
                }
            }
            foreach (string dir in gameDirectory) {
                List<string> _worlds = Directory.GetDirectories($"{dir}\\saves").ToList();
                foreach (string worldPath in _worlds) {
                    worlds.Add(new World(Util.TrimDoubleQuotationMarks(worldPath)));
                }
            }
            //foreach(var a in worlds) {
            //    Logger.Info($"world[{a.WName}]");
            //}
            return worlds;
        }

        /// <summary>
        /// PCからワールドデータ一覧を取得
        /// </summary>
        /// <param name="gameDirectory"></param>
        /// <returns>取得したList<world></returns>
        private static List<World> GetWorldDataFromHDD(List<string> gameDirectory) {
            List<World> worlds = new List<World>();
            Logger.Debug("call:GetWorldDataFromPC");
            foreach (string dir in gameDirectory) {
                if (Directory.Exists($"{dir}\\saves")) {
                    List<string> _worlds = Directory.GetDirectories($"{dir}\\saves").ToList();
                    foreach (string worldPath in _worlds) {
                        worlds.Add(new World(Util.TrimDoubleQuotationMarks(worldPath)));
                    }
                }
            }
            //foreach(var a in worlds) {
            //    Logger.Info($"world[{a.WName}]");
            //}
            return worlds;
        }

        public static void ConsoleConfig() {
            Logger.Info("----Configs----");
            foreach (World w in configs) {
                Logger.Info($"[{w.WDoBackup},{w.WName},{w.WPath},{w.WDir},]");
            }
            Logger.Info("---------------");
        }
        /// <summary>
        /// ワールドのバックアップソースが生きているかどうか
        /// </summary>
        /// <param name="w"></param>
        /// <returns></returns>
        public static bool isBackupAlive(World w) {
            if (w.isAlive) {
                //Logger.Info("info[DEBUG]:バックアップは死んでいます");
                return false;
            }
            else {
                //Logger.Info("info[DEBUG]:バックアップは生きています");
                return true;
            }
        }
    }


    public class World {
        public bool WDoBackup { get; set; }
        public string WPath { get; set; }
        public string WName { get; set; }
        public string WDir { get; set; }
        public bool isAlive { get; set; }
        public World(string path) {
            //if (!Directory.Exists(path)) {
            //    Logger.Info($"不正なpath[{path}]が渡されました");
            //    return;
            //}
            WDoBackup = true;
            WPath = path;
            WName = Path.GetFileName(path);
            WDir = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)));
            isAlive = true;
        }

        public World(string path, bool doBackup, bool _isAlive) {
            //if (!Directory.Exists(path)) {
            //    Logger.Info($"不正なpath[{path}]が渡されました");
            //    return;
            //}
            WDoBackup = doBackup;
            WPath = path;
            WName = Path.GetFileName(path);
            WDir = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)));
            isAlive = _isAlive;
        }
    }
}
