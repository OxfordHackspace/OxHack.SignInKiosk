﻿using Caliburn.Micro;
using OxHack.SignInKiosk.Events;
using OxHack.SignInKiosk.Services;
using OxHack.SignInKiosk.ViewModels;
using OxHack.SignInKiosk.Views;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Notifications;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace OxHack.SignInKiosk
{
	public sealed partial class App
	{
		private WinRTContainer container;
		private IEventAggregator eventAggregator;

		public App()
		{
			this.InitializeComponent();
			this.Suspending += this.SuspendedHandler;
		}

		protected override void Configure()
		{
			base.Configure();

			this.container = new WinRTContainer();
			this.container.RegisterWinRTServices();

			this.container.PerRequest<StartViewModel>();
			this.container.PerRequest<DisconnectedViewModel>();
			this.container.PerRequest<NameEntryViewModel>();
			this.container.PerRequest<SignedInGreetingViewModel>();
			this.container.PerRequest<SignedOutFarewellViewModel>();
			this.container.PerRequest<SignOutViewModel>();
			this.container.PerRequest<ToastService>();

			this.container.Singleton<MessageBrokerService>();
			this.container.Singleton<MessageOrchestratorService>();
			this.container.Singleton<TokenHolderService>();
			this.container.Singleton<SignInService>();
			this.container.Singleton<SoundEffectsService>();

			// TODO: Move this hardcoded URI to somesort of configuration file
			var serviceBase = new Uri("http://SignInKioskWebApi");
			this.container.RegisterHandler(typeof(SignInApiWrapper), null, c => new SignInApiWrapper(serviceBase));
			this.container.RegisterHandler(typeof(TokenHolderApiWrapper), null, c => new TokenHolderApiWrapper(serviceBase));
			this.container.RegisterHandler(typeof(ToastNotifier), null, c => ToastNotificationManager.CreateToastNotifier());

			this.eventAggregator = this.container.GetInstance<IEventAggregator>();
		}

		protected override object GetInstance(Type service, string key)
		{
			var instance = this.container.GetInstance(service, key);

			if (instance == null)
			{
				throw new InvalidOperationException($"Could not locate any instances of '{service.Name}' with key '{ key }'.");
			}

			return instance;
		}

		protected override IEnumerable<object> GetAllInstances(Type service)
		{
			return this.container.GetAllInstances(service);
		}

		protected override void BuildUp(object instance)
		{
			this.container.BuildUp(instance);
		}

		protected override void PrepareViewFirst(Frame rootFrame)
		{
			this.container.RegisterNavigationService(rootFrame);
		}

		protected async override void OnLaunched(LaunchActivatedEventArgs e)
		{
			//#if DEBUG
			//			if (System.Diagnostics.Debugger.IsAttached)
			//			{
			//				this.DebugSettings.EnableFrameRateCounter = true;
			//			}
			//#endif

			if (e.PreviousExecutionState != ApplicationExecutionState.Running)
			{
				Window.Current.VisibilityChanged += (s, ea) => this.eventAggregator.PublishOnCurrentThread(new VisibilityChanged(ea.Visible));
				this.Resuming += (s, ea) => this.eventAggregator.BeginPublishOnUIThread(new AppResumed());

				this.DisplayRootView<StartView>();
				await this.EnsureSingletonsAreRunning();
			}
		}

		private async Task EnsureSingletonsAreRunning()
		{
			this.container.GetInstance<MessageOrchestratorService>();
			this.container.GetInstance<TokenHolderService>();
			var soundEffectsService = this.container.GetInstance<SoundEffectsService>();

			this.RootFrame.Resources.Add("navigationSoundPlayer", soundEffectsService.NavigationSoundPlayer);
			this.RootFrame.Resources.Add("tokenReadSoundPlayer", soundEffectsService.TokenReadSoundPlayer);
		}

		private void SuspendedHandler(object sender, SuspendingEventArgs e)
		{
			var deferral = e.SuspendingOperation.GetDeferral();

			// TODO: Save application state and stop any background activity
			deferral.Complete();
		}
	}
}