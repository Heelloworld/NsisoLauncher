﻿using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using NsisoLauncher.Config;
using NsisoLauncher.Windows;
using NsisoLauncherCore;
using NsisoLauncherCore.Auth;
using NsisoLauncherCore.Modules;
using NsisoLauncherCore.Net;
using NsisoLauncherCore.Net.MojangApi.Api;
using NsisoLauncherCore.Net.MojangApi.Endpoints;
using NsisoLauncherCore.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NsisoLauncher
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : MetroWindow
    {

        //TODO:增加取消启动按钮
        public MainWindow()
        {
            InitializeComponent();
            App.LogHandler.AppendDebug("启动器主窗体已载入");
            mainPanel.Launch += MainPanel_Launch;
            App.Handler.GameExit += Handler_GameExit;
            CustomizeRefresh();
        }

        private async void MainPanel_Launch(object sender, Controls.LaunchEventArgs obj)
        {
            await LaunchGameFromArgs(obj);
        }

        #region 启动核心事件处理
        private void Handler_GameExit(object sender, GameExitArg arg)
        {
            this.Dispatcher.Invoke(new Action(() =>
            {
                this.WindowState = WindowState.Normal;
                if (!arg.IsNormalExit())
                {
                    this.ShowMessageAsync("游戏非正常退出",
                        string.Format("这很有可能是因为游戏崩溃导致的，退出代码:{0}，游戏持续时间:{1}",
                        arg.ExitCode, arg.Duration));
                }
            }));
        }
        #endregion

        #region 自定义
        public async void CustomizeRefresh()
        {
            if (!string.IsNullOrWhiteSpace(App.Config.MainConfig.Customize.LauncherTitle))
            {
                this.Title = App.Config.MainConfig.Customize.LauncherTitle;
            }
            if (App.Config.MainConfig.Customize.CustomBackGroundPicture)
            {
                string[] files = Directory.GetFiles(Path.GetDirectoryName(App.Config.MainConfigPath), "bgpic_?.png");
                if (files.Count() != 0)
                {
                    Random random = new Random();
                    ImageBrush brush = new ImageBrush(new BitmapImage(new Uri(files[random.Next(files.Count())])))
                    { TileMode = TileMode.FlipXY, AlignmentX = AlignmentX.Right, Stretch = Stretch.UniformToFill };
                    this.Background = brush;
                }
            }

            if (App.Config.MainConfig.User.Nide8ServerDependence)
            {
                try
                {
                    var lockAuthNode = App.Config.MainConfig.User.GetLockAuthNode();
                    if ((lockAuthNode != null) &&
                        (lockAuthNode.AuthType == AuthenticationType.NIDE8))
                    {
                        Config.Server nide8Server = new Config.Server() { ShowServerInfo = true };
                        var nide8ReturnResult = await (new NsisoLauncherCore.Net.Nide8API.APIHandler(lockAuthNode.Property["nide8ID"])).GetInfoAsync();
                        if (!string.IsNullOrWhiteSpace(nide8ReturnResult.Meta.ServerIP))
                        {
                            string[] serverIp = nide8ReturnResult.Meta.ServerIP.Split(':');
                            if (serverIp.Length == 2)
                            {
                                nide8Server.Address = serverIp[0];
                                nide8Server.Port = ushort.Parse(serverIp[1]);
                            }
                            else
                            {
                                nide8Server.Address = nide8ReturnResult.Meta.ServerIP;
                                nide8Server.Port = 25565;
                            }
                            nide8Server.ServerName = nide8ReturnResult.Meta.ServerName;
                            serverInfoControl.SetServerInfo(nide8Server);
                        }
                    }

                }
                catch (Exception)
                { }
            }
            else if (App.Config.MainConfig.Server != null)
            {
                serverInfoControl.SetServerInfo(App.Config.MainConfig.Server);
            }

            if (App.Config.MainConfig.Customize.CustomBackGroundMusic)
            {
                string[] files = Directory.GetFiles(Path.GetDirectoryName(App.Config.MainConfigPath), "bgmusic_?.mp3");
                if (files.Count() != 0)
                {
                    Random random = new Random();
                    mediaElement.Source = new Uri(files[random.Next(files.Count())]);
                    this.volumeButton.Visibility = Visibility.Visible;
                    mediaElement.Play();
                    mediaElement.Volume = 0;
                    await Task.Factory.StartNew(() =>
                    {
                        try
                        {
                            for (int i = 0; i < 50; i++)
                            {
                                this.Dispatcher.Invoke(new Action(() =>
                                {
                                    this.mediaElement.Volume += 0.01;
                                }));
                                Thread.Sleep(50);
                            }
                        }
                        catch (Exception) { }
                    });
                }
            }

        }

        private void volumeButton_Click(object sender, RoutedEventArgs e)
        {
            this.mediaElement.IsMuted = !this.mediaElement.IsMuted;
        }

        private void mediaElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            mediaElement.Stop();
            mediaElement.Play();
        }


        #endregion

        private async Task LaunchGameFromArgs(Controls.LaunchEventArgs args)
        {
            try
            {
                #region 检查有效数据
                if (args.LaunchVersion == null)
                {
                    await this.ShowMessageAsync(App.GetResourceString("String.Message.EmptyLaunchVersion"),
                        App.GetResourceString("String.Message.EmptyLaunchVersion2"));
                    return;
                }
                if (args.UserNode == null)
                {
                    await this.ShowMessageAsync(App.GetResourceString("String.Message.EmptyUsername"),
                        App.GetResourceString("String.Message.EmptyUsername2"));
                    return;
                }
                if (args.AuthNode == null)
                {
                    await this.ShowMessageAsync(App.GetResourceString("String.Message.EmptyAuthType"),
                        App.GetResourceString("String.Message.EmptyAuthType2"));
                    return;
                }
                if (App.Handler.Java == null)
                {
                    await this.ShowMessageAsync(App.GetResourceString("String.Message.NoJava"), App.GetResourceString("String.Message.NoJava2"));
                    return;
                }
                #endregion


                #region 保存启动数据
                App.Config.MainConfig.History.LastLaunchVersion = args.LaunchVersion.ID;
                #endregion

                LaunchSetting launchSetting = new LaunchSetting()
                {
                    Version = args.LaunchVersion
                };

                this.loadingGrid.Visibility = Visibility.Visible;
                this.loadingRing.IsActive = true;

                #region 验证

                #region 设置ClientToken
                if (string.IsNullOrWhiteSpace(App.Config.MainConfig.User.ClientToken))
                {
                    App.Config.MainConfig.User.ClientToken = Guid.NewGuid().ToString("N");
                }
                else
                {
                    Requester.ClientToken = App.Config.MainConfig.User.ClientToken;
                }
                #endregion

                #region 多语言支持变量
                LoginDialogSettings loginDialogSettings = new LoginDialogSettings()
                {
                    NegativeButtonText = App.GetResourceString("String.Base.Cancel"),
                    AffirmativeButtonText = App.GetResourceString("String.Base.Login"),
                    RememberCheckBoxText = App.GetResourceString("String.Base.ShouldRememberLogin"),
                    UsernameWatermark = App.GetResourceString("String.Base.Username"),
                    InitialUsername = args.UserNode.UserName,
                    RememberCheckBoxVisibility = Visibility,
                    EnablePasswordPreview = true,
                    PasswordWatermark = App.GetResourceString("String.Base.Password"),
                    NegativeButtonVisibility = Visibility.Visible
                };
                #endregion

                //主验证器接口
                IAuthenticator authenticator = null;
                bool shouldRemember = false;

                //bool isSameAuthType = (authNode.AuthenticationType == auth);
                bool isRemember = (!string.IsNullOrWhiteSpace(args.UserNode.AccessToken)) && (args.UserNode.SelectProfileUUID != null);
                //bool isSameName = userName == App.Config.MainConfig.User.UserName;

                switch (args.AuthNode.AuthType)
                {
                    #region 离线验证
                    case AuthenticationType.OFFLINE:
                        if (args.IsNewUser)
                        {
                            authenticator = new OfflineAuthenticator(args.UserNode.UserName);
                        }
                        else
                        {
                            authenticator = new OfflineAuthenticator(args.UserNode.UserName,
                                args.UserNode.UserData,
                                args.UserNode.SelectProfileUUID);
                        }
                        break;
                    #endregion

                    #region MOJANG验证
                    case AuthenticationType.MOJANG:
                        if (isRemember)
                        {
                            var mYggTokenAuthenticator = new YggdrasilTokenAuthenticator(args.UserNode.AccessToken,
                                args.UserNode.GetSelectProfileUUID(),
                                args.UserNode.UserData);
                            mYggTokenAuthenticator.ProxyAuthServerAddress = "https://authserver.mojang.com";
                            authenticator = mYggTokenAuthenticator;
                        }
                        else
                        {
                            var mojangLoginDResult = await this.ShowLoginAsync(App.GetResourceString("String.Mainwindow.Auth.Mojang.Login"),
                                App.GetResourceString("String.Mainwindow.Auth.Mojang.Login2"),
                                loginDialogSettings);
                            if (IsValidateLoginData(mojangLoginDResult))
                            {
                                var mYggAuthenticator = new YggdrasilAuthenticator(new Credentials()
                                {
                                    Username = mojangLoginDResult.Username,
                                    Password = mojangLoginDResult.Password
                                });
                                mYggAuthenticator.ProxyAuthServerAddress = "https://authserver.mojang.com";
                                authenticator = mYggAuthenticator;
                                shouldRemember = mojangLoginDResult.ShouldRemember;
                            }
                            else
                            {
                                await this.ShowMessageAsync("您输入的账号或密码为空", "请检查您是否正确填写登录信息");
                                return;
                            }
                        }
                        break;
                    #endregion

                    #region NIDE8验证
                    case AuthenticationType.NIDE8:
                        string nide8ID = args.AuthNode.Property["nide8ID"];
                        if (string.IsNullOrWhiteSpace(nide8ID))
                        {
                            await this.ShowMessageAsync(App.GetResourceString("String.Mainwindow.Auth.Nide8.NoID"),
                                App.GetResourceString("String.Mainwindow.Auth.Nide8.NoID2"));
                            return;
                        }
                        var nide8ChooseResult = await this.ShowMessageAsync(App.GetResourceString("String.Mainwindow.Auth.Nide8.Login2"), App.GetResourceString("String.Base.Choose"),
                            MessageDialogStyle.AffirmativeAndNegativeAndSingleAuxiliary,
                            new MetroDialogSettings()
                            {
                                AffirmativeButtonText = App.GetResourceString("String.Base.Login"),
                                NegativeButtonText = App.GetResourceString("String.Base.Cancel"),
                                FirstAuxiliaryButtonText = App.GetResourceString("String.Base.Register"),
                                DefaultButtonFocus = MessageDialogResult.Affirmative
                            });
                        switch (nide8ChooseResult)
                        {
                            case MessageDialogResult.Canceled:
                                return;
                            case MessageDialogResult.Negative:
                                return;
                            case MessageDialogResult.FirstAuxiliary:
                                System.Diagnostics.Process.Start(string.Format("https://login2.nide8.com:233/{0}/register", nide8ID));
                                return;
                            case MessageDialogResult.Affirmative:
                                if (isRemember)
                                {
                                    var nYggTokenCator = new Nide8TokenAuthenticator(nide8ID, args.UserNode.AccessToken,
                                        args.UserNode.GetSelectProfileUUID(),
                                        args.UserNode.UserData);
                                    authenticator = nYggTokenCator;
                                }
                                else
                                {
                                    var nide8LoginDResult = await this.ShowLoginAsync(App.GetResourceString("String.Mainwindow.Auth.Nide8.Login"),
                                        App.GetResourceString("String.Mainwindow.Auth.Nide8.Login2"),
                                        loginDialogSettings);
                                    if (IsValidateLoginData(nide8LoginDResult))
                                    {
                                        var nYggCator = new Nide8Authenticator(
                                            nide8ID,
                                            new Credentials()
                                            {
                                                Username = nide8LoginDResult.Username,
                                                Password = nide8LoginDResult.Password
                                            });
                                        authenticator = nYggCator;
                                        shouldRemember = nide8LoginDResult.ShouldRemember;
                                    }
                                    else
                                    {
                                        await this.ShowMessageAsync("您输入的账号或密码为空", "请检查您是否正确填写登录信息");
                                        return;
                                    }
                                }
                                break;
                            default:
                                break;
                        }
                        break;
                    #endregion

                    #region AUTHLIB验证
                    case AuthenticationType.AUTHLIB_INJECTOR:
                        string aiRootAddr = args.AuthNode.Property["authserver"];
                        if (string.IsNullOrWhiteSpace(aiRootAddr))
                        {
                            await this.ShowMessageAsync(App.GetResourceString("String.Mainwindow.Auth.Custom.NoAdrress"),
                                App.GetResourceString("String.Mainwindow.Auth.Custom.NoAdrress2"));
                            return;
                        }
                        else
                        {
                            if (isRemember)
                            {
                                var cYggTokenCator = new AuthlibInjectorTokenAuthenticator(aiRootAddr,
                                    args.UserNode.AccessToken,
                                    args.UserNode.GetSelectProfileUUID(),
                                    args.UserNode.UserData);
                                authenticator = cYggTokenCator;
                            }
                            else
                            {
                                var customLoginDResult = await this.ShowLoginAsync(App.GetResourceString("String.Mainwindow.Auth.Custom.Login"),
                               App.GetResourceString("String.Mainwindow.Auth.Custom.Login2"),
                               loginDialogSettings);
                                if (IsValidateLoginData(customLoginDResult))
                                {
                                    var cYggAuthenticator = new AuthlibInjectorAuthenticator(
                                        aiRootAddr,
                                        new Credentials()
                                        {
                                            Username = customLoginDResult.Username,
                                            Password = customLoginDResult.Password
                                        });
                                    authenticator = cYggAuthenticator;
                                    shouldRemember = customLoginDResult.ShouldRemember;
                                }
                                else
                                {
                                    await this.ShowMessageAsync("您输入的账号或密码为空", "请检查您是否正确填写登录信息");
                                    return;
                                }
                            }
                        }
                        break;
                    #endregion

                    #region 自定义验证
                    case AuthenticationType.CUSTOM_SERVER:
                        string customAuthServer = args.AuthNode.Property["authserver"];
                        if (string.IsNullOrWhiteSpace(customAuthServer))
                        {
                            await this.ShowMessageAsync(App.GetResourceString("String.Mainwindow.Auth.Custom.NoAdrress"),
                                App.GetResourceString("String.Mainwindow.Auth.Custom.NoAdrress2"));
                            return;
                        }
                        else
                        {
                            if (isRemember)
                            {
                                var cYggTokenCator = new YggdrasilTokenAuthenticator(args.UserNode.AccessToken,
                                args.UserNode.GetSelectProfileUUID(),
                                args.UserNode.UserData);
                                cYggTokenCator.ProxyAuthServerAddress = customAuthServer;
                            }
                            else
                            {
                                var customLoginDResult = await this.ShowLoginAsync(App.GetResourceString("String.Mainwindow.Auth.Custom.Login"),
                               App.GetResourceString("String.Mainwindow.Auth.Custom.Login2"),
                               loginDialogSettings);
                                if (IsValidateLoginData(customLoginDResult))
                                {
                                    var cYggAuthenticator = new YggdrasilAuthenticator(new Credentials()
                                    {
                                        Username = customLoginDResult.Username,
                                        Password = customLoginDResult.Password
                                    });
                                    cYggAuthenticator.ProxyAuthServerAddress = customAuthServer;
                                    authenticator = cYggAuthenticator;
                                    shouldRemember = customLoginDResult.ShouldRemember;
                                }
                                else
                                {
                                    await this.ShowMessageAsync("您输入的账号或密码为空", "请检查您是否正确填写登录信息");
                                    return;
                                }
                            }
                        }
                        break;
                    #endregion

                    #region 意外情况
                    default:
                        if (args.IsNewUser)
                        {
                            authenticator = new OfflineAuthenticator(args.UserNode.UserName);
                        }
                        else
                        {
                            authenticator = new OfflineAuthenticator(args.UserNode.UserName,
                                args.UserNode.UserData,
                                args.UserNode.SelectProfileUUID);
                        }
                        break;
                        #endregion
                }

                //如果验证方式不是离线验证
                if (args.AuthNode.AuthType != AuthenticationType.OFFLINE)
                {
                    string currentLoginType = string.Format("正在进行{0}中...", args.AuthNode.Name);
                    string loginMsg = "这需要联网进行操作，可能需要一分钟的时间";
                    var loader = await this.ShowProgressAsync(currentLoginType, loginMsg, true);

                    loader.SetIndeterminate();
                    var authResult = await authenticator.DoAuthenticateAsync();
                    await loader.CloseAsync();

                    switch (authResult.State)
                    {
                        case AuthState.SUCCESS:
                            #region 检验
                            if (authResult.SelectedProfileUUID == null)
                            {
                                if (authResult.Profiles == null || authResult.Profiles.Count == 0)
                                {
                                    await this.ShowMessageAsync("验证失败：您没有可用的游戏角色（Profile）",
                                    "如果您是正版验证，则您可能还未购买游戏本体。如果您是外置登录，则您可能未设置可用角色");
                                    return;
                                }
                                await this.ShowMessageAsync("验证失败：您没有选中任何游戏角色（Profile）",
                                "请选中您要进行游戏的角色，目前Nsiso启动器不支持修改游戏角色");
                                return;
                            }
                            #endregion
                            args.UserNode.SelectProfileUUID = authResult.SelectedProfileUUID.Value;
                            args.UserNode.UserData = authResult.UserData;
                            if (authResult.Profiles != null)
                            {
                                args.UserNode.Profiles.Clear();
                                authResult.Profiles.ForEach(x => args.UserNode.Profiles.Add(x.Value, x));
                            }
                            if (shouldRemember)
                            {
                                args.UserNode.AccessToken = authResult.AccessToken;
                            }
                            launchSetting.AuthenticateResult = authResult;
                            break;
                        case AuthState.REQ_LOGIN:
                            args.UserNode.ClearAuthCache();
                            await this.ShowMessageAsync("验证失败：您的登录信息已过期",
                                string.Format("请您重新进行登录。具体信息：{0}", authResult.Error.ErrorMessage));
                            return;
                        case AuthState.ERR_INVALID_CRDL:
                            await this.ShowMessageAsync("验证失败：您的登录账号或密码错误",
                                string.Format("请您确认您输入的账号密码正确。具体信息：{0}", authResult.Error.ErrorMessage));
                            return;
                        case AuthState.ERR_NOTFOUND:
                            if (args.AuthNode.AuthType == AuthenticationType.CUSTOM_SERVER || args.AuthNode.AuthType == AuthenticationType.AUTHLIB_INJECTOR)
                            {
                                await this.ShowMessageAsync("验证失败：代理验证服务器地址有误或账号未找到",
                                string.Format("请确认您的Authlib-Injector验证服务器（Authlib-Injector验证）或自定义验证服务器（自定义验证）地址正确或确认账号和游戏角色存在。具体信息：{0}",
                                authResult.Error.ErrorMessage));
                            }
                            else
                            {
                                await this.ShowMessageAsync("验证失败：您的账号未找到",
                                string.Format("请确认您的账号和游戏角色存在。具体信息：{0}", authResult.Error.ErrorMessage));
                            }
                            return;
                        case AuthState.ERR_OTHER:
                            await this.ShowMessageAsync("验证失败：其他错误",
                                string.Format("具体信息：{0}", authResult.Error.ErrorMessage));
                            return;
                        case AuthState.ERR_INSIDE:
                            await this.ShowMessageAsync("验证失败：启动器内部错误",
                                string.Format("建议您联系启动器开发者进行解决。具体信息：{0}", authResult.Error.ErrorMessage));
                            return;
                        default:
                            await this.ShowMessageAsync("验证失败：未知错误",
                                "建议您联系启动器开发者进行解决。");
                            return;
                    }
                }
                else
                {
                    var authResult = await authenticator.DoAuthenticateAsync();
                    launchSetting.AuthenticateResult = authResult;
                    args.UserNode.UserData = authResult.UserData;
                    args.UserNode.SelectProfileUUID = authResult.SelectedProfileUUID.Value;
                }

                App.Config.MainConfig.History.SelectedUserNodeID = args.UserNode.UserData.ID;
                if (!App.Config.MainConfig.User.UserDatabase.ContainsKey(args.UserNode.UserData.ID))
                {
                    App.Config.MainConfig.User.UserDatabase.Add(args.UserNode.UserData.ID, args.UserNode);
                }
                #endregion

                #region 检查游戏完整
                List<DownloadTask> losts = new List<DownloadTask>();

                App.LogHandler.AppendInfo("检查丢失的依赖库文件中...");
                var lostDepend = await FileHelper.GetLostDependDownloadTaskAsync(
                    App.Config.MainConfig.Download.DownloadSource,
                    App.Handler,
                    launchSetting.Version);

                if (args.AuthNode.AuthType == AuthenticationType.NIDE8)
                {
                    string nideJarPath = App.Handler.GetNide8JarPath();
                    if (!File.Exists(nideJarPath))
                    {
                        lostDepend.Add(new DownloadTask("统一通行证核心", "https://login2.nide8.com:233/index/jar", nideJarPath));
                    }
                }
                else if (args.AuthNode.AuthType == AuthenticationType.AUTHLIB_INJECTOR)
                {
                    string aiJarPath = App.Handler.GetAIJarPath();
                    if (!File.Exists(aiJarPath))
                    {
                        lostDepend.Add(await NsisoLauncherCore.Net.Tools.GetDownloadUrl.GetAICoreDownloadTask(App.Config.MainConfig.Download.DownloadSource, aiJarPath));
                    }
                }

                if (App.Config.MainConfig.Environment.DownloadLostDepend && lostDepend.Count != 0)
                {
                    MessageDialogResult downDependResult = await this.ShowMessageAsync(App.GetResourceString("String.Mainwindow.NeedDownloadDepend"),
                        App.GetResourceString("String.Mainwindow.NeedDownloadDepend2"),
                        MessageDialogStyle.AffirmativeAndNegativeAndSingleAuxiliary, new MetroDialogSettings()
                        {
                            AffirmativeButtonText = App.GetResourceString("String.Base.Download"),
                            NegativeButtonText = App.GetResourceString("String.Base.Cancel"),
                            FirstAuxiliaryButtonText = App.GetResourceString("String.Base.Unremember"),
                            DefaultButtonFocus = MessageDialogResult.Affirmative
                        });
                    switch (downDependResult)
                    {
                        case MessageDialogResult.Affirmative:
                            losts.AddRange(lostDepend);
                            break;
                        case MessageDialogResult.FirstAuxiliary:
                            App.Config.MainConfig.Environment.DownloadLostDepend = false;
                            break;
                        default:
                            break;
                    }

                }

                App.LogHandler.AppendInfo("检查丢失的资源文件中...");
                if (App.Config.MainConfig.Environment.DownloadLostAssets && (await FileHelper.IsLostAssetsAsync(App.Config.MainConfig.Download.DownloadSource,
                    App.Handler, launchSetting.Version)))
                {
                    MessageDialogResult downDependResult = await this.ShowMessageAsync(App.GetResourceString("String.Mainwindow.NeedDownloadAssets"),
                        App.GetResourceString("String.Mainwindow.NeedDownloadAssets2"),
                        MessageDialogStyle.AffirmativeAndNegativeAndSingleAuxiliary, new MetroDialogSettings()
                        {
                            AffirmativeButtonText = App.GetResourceString("String.Base.Download"),
                            NegativeButtonText = App.GetResourceString("String.Base.Cancel"),
                            FirstAuxiliaryButtonText = App.GetResourceString("String.Base.Unremember"),
                            DefaultButtonFocus = MessageDialogResult.Affirmative
                        });
                    switch (downDependResult)
                    {
                        case MessageDialogResult.Affirmative:
                            var lostAssets = await FileHelper.GetLostAssetsDownloadTaskAsync(
                                App.Config.MainConfig.Download.DownloadSource,
                                App.Handler, launchSetting.Version);
                            losts.AddRange(lostAssets);
                            break;
                        case MessageDialogResult.FirstAuxiliary:
                            App.Config.MainConfig.Environment.DownloadLostAssets = false;
                            break;
                        default:
                            break;
                    }

                }

                if (losts.Count != 0)
                {
                    if (!App.Downloader.IsBusy)
                    {
                        App.Downloader.AddDownloadTask(losts);
                        App.Downloader.StartDownload();
                        var downloadResult = await new DownloadWindow().ShowWhenDownloading();
                        if (downloadResult?.ErrorList?.Count != 0)
                        {
                            await this.ShowMessageAsync(string.Format("有{0}个文件下载补全失败", downloadResult.ErrorList.Count),
                                "这可能是因为本地网络问题或下载源问题，您可以尝试检查网络环境或在设置中切换首选下载源，启动器将继续尝试启动");
                        }
                    }
                    else
                    {
                        await this.ShowMessageAsync("无法下载补全：当前有正在下载中的任务", "请等待其下载完毕或取消下载，启动器将尝试继续启动");
                    }
                }

                #endregion

                #region 根据配置文件设置
                launchSetting.AdvencedGameArguments += App.Config.MainConfig.Environment.AdvencedGameArguments;
                launchSetting.AdvencedJvmArguments += App.Config.MainConfig.Environment.AdvencedJvmArguments;
                launchSetting.GCArgument += App.Config.MainConfig.Environment.GCArgument;
                launchSetting.GCEnabled = App.Config.MainConfig.Environment.GCEnabled;
                launchSetting.GCType = App.Config.MainConfig.Environment.GCType;
                launchSetting.JavaAgent += App.Config.MainConfig.Environment.JavaAgent;
                if (args.AuthNode.AuthType == AuthenticationType.NIDE8)
                {
                    launchSetting.JavaAgent += string.Format(" \"{0}\"={1}", App.Handler.GetNide8JarPath(), args.AuthNode.Property["nide8ID"]);
                }
                else if (args.AuthNode.AuthType == AuthenticationType.AUTHLIB_INJECTOR)
                {
                    launchSetting.JavaAgent += string.Format(" \"{0}\"={1}", App.Handler.GetAIJarPath(), args.AuthNode.Property["authserver"]);
                }

                //直连服务器设置
                var lockAuthNode = App.Config.MainConfig.User.GetLockAuthNode();
                if (App.Config.MainConfig.User.Nide8ServerDependence &&
                    (lockAuthNode != null) &&
                        (lockAuthNode.AuthType == AuthenticationType.NIDE8))
                {
                    var nide8ReturnResult = await (new NsisoLauncherCore.Net.Nide8API.APIHandler(lockAuthNode.Property["nide8ID"])).GetInfoAsync();
                    if (!string.IsNullOrWhiteSpace(nide8ReturnResult.Meta.ServerIP))
                    {
                        NsisoLauncherCore.Modules.Server server = new NsisoLauncherCore.Modules.Server();
                        string[] serverIp = nide8ReturnResult.Meta.ServerIP.Split(':');
                        if (serverIp.Length == 2)
                        {
                            server.Address = serverIp[0];
                            server.Port = ushort.Parse(serverIp[1]);
                        }
                        else
                        {
                            server.Address = nide8ReturnResult.Meta.ServerIP;
                            server.Port = 25565;
                        }
                        launchSetting.LaunchToServer = server;
                    }
                }
                else if (App.Config.MainConfig.Server.LaunchToServer)
                {
                    launchSetting.LaunchToServer = new NsisoLauncherCore.Modules.Server() { Address = App.Config.MainConfig.Server.Address, Port = App.Config.MainConfig.Server.Port };
                }

                //自动内存设置
                if (App.Config.MainConfig.Environment.AutoMemory)
                {
                    var m = SystemTools.GetBestMemory(App.Handler.Java);
                    App.Config.MainConfig.Environment.MaxMemory = m;
                    launchSetting.MaxMemory = m;
                }
                else
                {
                    launchSetting.MaxMemory = App.Config.MainConfig.Environment.MaxMemory;
                }
                launchSetting.VersionType = App.Config.MainConfig.Customize.VersionInfo;
                launchSetting.WindowSize = App.Config.MainConfig.Environment.WindowSize;
                #endregion

                #region 配置文件处理
                App.Config.Save();
                #endregion

                #region 启动

                App.LogHandler.OnLog += (a, b) => { this.Invoke(() => { launchInfoBlock.Text = b.Message; }); };
                var result = await App.Handler.LaunchAsync(launchSetting);
                App.LogHandler.OnLog -= (a, b) => { this.Invoke(() => { launchInfoBlock.Text = b.Message; }); };

                //程序猿是找不到女朋友的了 :) 
                if (!result.IsSuccess)
                {
                    await this.ShowMessageAsync(App.GetResourceString("String.Mainwindow.LaunchError") + result.LaunchException.Title, result.LaunchException.Message);
                    App.LogHandler.AppendError(result.LaunchException);
                }
                else
                {
                    cancelLaunchButton.Click += (x, y) => { CancelLaunching(result); };

                    #region 等待游戏响应
                    try
                    {
                        await Task.Factory.StartNew(() =>
                        {
                            result.Process.WaitForInputIdle();
                        });
                    }
                    catch (Exception ex)
                    {
                        await this.ShowMessageAsync("启动后等待游戏窗口响应异常", "这可能是由于游戏进程发生意外（闪退）导致的。具体原因:" + ex.Message);
                        return;
                    }
                    #endregion

                    cancelLaunchButton.Click -= (x, y) => { CancelLaunching(result); };

                    #region 数据反馈
                    //API使用次数计数器+1
                    await App.NsisoAPIHandler.RefreshUsingTimesCounter();
                    #endregion

                    if (App.Config.MainConfig.Environment.ExitAfterLaunch)
                    {
                        Application.Current.Shutdown();
                    }
                    this.WindowState = WindowState.Minimized;

                    mainPanel.Refresh();

                    //自定义处理
                    if (!string.IsNullOrWhiteSpace(App.Config.MainConfig.Customize.GameWindowTitle))
                    {
                        GameHelper.SetGameTitle(result, App.Config.MainConfig.Customize.GameWindowTitle);
                    }
                    if (App.Config.MainConfig.Customize.CustomBackGroundMusic)
                    {
                        mediaElement.Volume = 0.5;
                        await Task.Factory.StartNew(() =>
                        {
                            try
                            {
                                for (int i = 0; i < 50; i++)
                                {
                                    this.Dispatcher.Invoke(new Action(() =>
                                    {
                                        this.mediaElement.Volume -= 0.01;
                                    }));
                                    Thread.Sleep(50);
                                }
                                this.Dispatcher.Invoke(new Action(() =>
                                {
                                    this.mediaElement.Stop();
                                }));
                            }
                            catch (Exception) { }
                        });
                    }
                }
                #endregion
            }
            catch (Exception ex)
            {
                App.LogHandler.AppendFatal(ex);
            }
            finally
            {
                this.loadingGrid.Visibility = Visibility.Hidden;
                this.loadingRing.IsActive = false;
            }
        }



        #region MainWindow event

        private async void mainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            #region 无JAVA提示
            if (App.Handler.Java == null)
            {
                var result = await this.ShowMessageAsync(App.GetResourceString("String.Message.NoJava"),
                    App.GetResourceString("String.Message.NoJava2"),
                    MessageDialogStyle.AffirmativeAndNegative,
                    new MetroDialogSettings()
                    {
                        AffirmativeButtonText = App.GetResourceString("String.Base.Yes"),
                        NegativeButtonText = App.GetResourceString("String.Base.Cancel"),
                        DefaultButtonFocus = MessageDialogResult.Affirmative
                    });
                if (result == MessageDialogResult.Affirmative)
                {
                    var arch = SystemTools.GetSystemArch();
                    switch (arch)
                    {
                        case ArchEnum.x32:
                            App.Downloader.AddDownloadTask(new DownloadTask("32位JAVA安装包", @"https://bmclapi.bangbang93.com/java/jre_x86.exe", "jre_x86.exe"));
                            App.Downloader.StartDownload();
                            await new DownloadWindow().ShowWhenDownloading();
                            System.Diagnostics.Process.Start("Explorer.exe", "jre_x86.exe");
                            break;
                        case ArchEnum.x64:
                            App.Downloader.AddDownloadTask(new DownloadTask("64位JAVA安装包", @"https://bmclapi.bangbang93.com/java/jre_x64.exe", "jre_x64.exe"));
                            App.Downloader.StartDownload();
                            await new DownloadWindow().ShowWhenDownloading();
                            System.Diagnostics.Process.Start("Explorer.exe", "jre_x64.exe");
                            break;
                        default:
                            break;
                    }
                }
            }
            #endregion

            #region 检查更新
            if (App.Config.MainConfig.Launcher.CheckUpdate)
            {
                await CheckUpdate();
            }
            #endregion
        }

        private void mainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (App.Downloader.IsBusy)
            {
                var choose = this.ShowModalMessageExternal("后台正在下载中", "是否确认关闭程序？这将会取消下载"
                , MessageDialogStyle.AffirmativeAndNegative,
                new MetroDialogSettings()
                {
                    AffirmativeButtonText = App.GetResourceString("String.Base.Yes"),
                    NegativeButtonText = App.GetResourceString("String.Base.Cancel")
                });
                if (choose == MessageDialogResult.Affirmative)
                {
                    App.Downloader.RequestCancel();
                }
                else
                {
                    e.Cancel = true;
                }
            }

        }
        #endregion

        #region Tools
        private bool IsValidateLoginData(LoginDialogData data)
        {
            if (data == null)
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(data.Username))
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(data.Password))
            {
                return false;
            }
            return true;
        }

        private void CancelLaunching(LaunchResult result)
        {
            if (!result.Process.HasExited)
            {
                result.Process.Kill();
            }
        }

        private async Task CheckUpdate()
        {
            var ver = await App.NsisoAPIHandler.GetLatestLauncherVersion();
            if (ver != null)
            {
                System.Version currentVersion = Application.ResourceAssembly.GetName().Version;
                if ((ver.Version > currentVersion) &&
                    ver.ReleaseType.Equals("release", StringComparison.OrdinalIgnoreCase))
                {
                    new UpdateWindow(ver).Show();
                }
            }
        }
        #endregion
    }
}
