﻿using OxHack.SignInKiosk.Messaging;
using OxHack.SignInKiosk.Messaging.Messages;
using System;
using System.Threading.Tasks;
using Prism.Events;
using OxHack.SignInKiosk.TokenReaderService.Events;
using System.Reactive.Linq;
using System.Security.Cryptography;
using NLog;

namespace OxHack.SignInKiosk.TokenReaderService.SubServices
{
	class MessageRelayer
	{
		private readonly ILogger logger = LogManager.GetCurrentClassLogger();
		private readonly MessagingClient messagingClient;
		private readonly MD5 hasher;
		private readonly TimeSpan deadPeriod;
		private DateTime lastPublishTime;

		public MessageRelayer(IEventAggregator eventAggregator, MessagingClient messagingClient)
		{
			this.messagingClient = messagingClient;

			this.hasher = MD5.Create();
			this.deadPeriod = TimeSpan.FromSeconds(3);
			this.lastPublishTime = DateTime.MinValue;

			var tokenReadSequence =
				Observable.FromEvent<uint>(
					addHandler => eventAggregator.GetEvent<TokenReadEvent>().Subscribe(addHandler),
					removeHandler => eventAggregator.GetEvent<TokenReadEvent>().Unsubscribe(removeHandler))
				.DistinctUntilChanged(keySelector => keySelector)
				.Where(item => item != 0)
				.Subscribe(this.OnTokenRead);
		}

		private async void OnTokenRead(uint tokenId)
		{
			var invocationTime = DateTime.Now;
			try
			{
				if ((invocationTime - lastPublishTime) >= deadPeriod)
				{
					var tokenIdHash = hasher.ComputeHash(BitConverter.GetBytes(tokenId));
					var formattedTokenIdHash = BitConverter.ToString(tokenIdHash).Replace("-", String.Empty);

					//this.logger.Debug($"Relaying token {item} as {formattedTokenIdHash}.");

					var message = new TokenRead(formattedTokenIdHash);
					await this.messagingClient.Publish(message);

					lastPublishTime = invocationTime;
				}
			}
			catch (Exception ex)
			{
				this.logger.Error(ex);
			}
		}

		public async Task Start()
		{
			await this.messagingClient.Connect();
		}

		public async Task Stop()
		{
			await this.messagingClient.Disconnect();
		}
	}
}