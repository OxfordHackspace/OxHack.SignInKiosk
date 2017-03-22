﻿using Caliburn.Micro;
using OxHack.SignInKiosk.Domain.Messages;
using OxHack.SignInKiosk.Events;
using OxHack.SignInKiosk.MessageBrokerProxy;
using System;
using System.ServiceModel;
using System.Threading.Tasks;

namespace OxHack.SignInKiosk.Services
{
	public class MessageBrokerService :
		IHandle<VisibilityChanged>,
		IHandle<AppResumed>
	{
		private readonly object connectionLock = new object();

		private ServiceCallback serviceCallback;
		private MessageBrokerProxyServiceClient serviceClient;
		private readonly IEventAggregator eventAggregator;
		private Task keepAliveWorker;

		public MessageBrokerService(IEventAggregator eventAggregator)
		{
			this.eventAggregator = eventAggregator;
			this.eventAggregator.Subscribe(this);
		}

		public async void Handle(VisibilityChanged message)
		{
			try
			{
				if (message.IsVisible)
				{
					await this.ConnectIfNeeded();
				}
				else
				{
					await this.Disconnect();
				}
			}
			catch
			{
				// ignore
			}
		}

		public async void Handle(AppResumed message)
		{
			try
			{
				await this.ConnectIfNeeded();
			}
			catch
			{
				// ignore
			}
		}

		public async Task Publish(SignInRequestSubmitted message)
		{
			await this.serviceClient.PublishSignInRequestAsync(message);
		}

		public async Task Publish(SignOutRequestSubmitted message)
		{
			await this.serviceClient.PublishSignOutRequestAsync(message);
		}

		private async Task KeepAliveWorkerLoop()
		{
			while (this.serviceClient != null)
			{
				try
				{
					await Task.Delay(TimeSpan.FromSeconds(25));
					await this.serviceClient.KeepAliveAsync();
				}
				catch
				{
					// Do nothing
				}
			}
		}

		internal async Task ConnectIfNeeded()
		{
			// if null, then connect.
			// if not null but still faulted, the regular faulted handler will handle reconnecting.
			// yes, this is kludgy.
			if (this.serviceClient == null || this.serviceClient.InnerChannel.State == CommunicationState.Faulted)
			{
				await this.CreateNewConnection();
			}
		}

		private async Task CreateNewConnection()
		{
			await this.Disconnect();

			lock (this.connectionLock)
			{
				var timeout = TimeSpan.FromSeconds(7);

				//TODO: Enable transport security.
				var binding = new NetTcpBinding()
				{
					MaxBufferSize = int.MaxValue,
					ReaderQuotas = System.Xml.XmlDictionaryReaderQuotas.Max,
					MaxReceivedMessageSize = int.MaxValue,
					Security = new NetTcpSecurity()
					{
						Message = new MessageSecurityOverTcp()
						{
							ClientCredentialType = MessageCredentialType.None
						},
						Transport = new TcpTransportSecurity()
						{
							ClientCredentialType = TcpClientCredentialType.None
						},
						Mode = SecurityMode.None,
					},
					CloseTimeout = timeout,
					OpenTimeout = timeout,
					ReceiveTimeout = timeout,
					SendTimeout = timeout,
				};

				// TODO: put this in a configuration file somewhere
				var remoteAddress = new EndpointAddress(new Uri("net.tcp://MessageBrokerProxyService:8137/MessageBrokerProxyService"));

				this.serviceCallback = new ServiceCallback(this.eventAggregator);
				this.serviceClient = new MessageBrokerProxyServiceClient(new InstanceContext(this.serviceCallback), binding, remoteAddress);
				this.serviceClient.InnerChannel.Faulted += this.HandleServiceClientFaults;
			}

			await this.serviceClient.SubscribeAsync();
			this.keepAliveWorker = Task.Run(this.KeepAliveWorkerLoop);
			await this.eventAggregator.PublishOnUIThreadAsync(new Connected());
		}

		private async Task Disconnect()
		{
			lock (this.connectionLock)
			{
				try
				{
					if (this.serviceClient != null)
					{
						this.serviceClient.InnerChannel.Faulted -= this.HandleServiceClientFaults;
						if (this.serviceClient.InnerChannel.State == CommunicationState.Faulted)
						{
							this.serviceClient.Abort();
						}
						else
						{
							this.serviceClient.UnsubscribeAsync();
							this.serviceClient.CloseAsync();
						}
					}
				}
				catch
				{
					// ignore
				}
				finally
				{
					this.serviceClient = null;
					this.serviceCallback = null;
				}
			}
			await this.eventAggregator.PublishOnUIThreadAsync(new Disconnected());
		}

		private async void HandleServiceClientFaults(object sender, EventArgs e)
		{
			await this.Disconnect();
		}

		private class ServiceCallback : IMessageBrokerProxyServiceCallback
		{
			private readonly IEventAggregator eventAggregator;

			public ServiceCallback(IEventAggregator eventAggregator)
			{
				this.eventAggregator = eventAggregator;
			}

			public async void OnPersonSignedInPublished(PersonSignedIn message)
			{
				await this.eventAggregator.PublishOnUIThreadAsync(message);
			}

			public async void OnPersonSignedOutPublished(PersonSignedOut message)
			{
				await this.eventAggregator.PublishOnUIThreadAsync(message);
			}

			public async void OnTokenReadPublished(TokenRead message)
			{
				await this.eventAggregator.PublishOnUIThreadAsync(message);
			}

			public async void OnSignInRequestSubmittedPublished(SignInRequestSubmitted message)
			{
				await this.eventAggregator.PublishOnUIThreadAsync(message);
			}

			public async void OnSignOutRequestSubmittedPublished(SignOutRequestSubmitted message)
			{
				await this.eventAggregator.PublishOnUIThreadAsync(message);
			}

			public void KeepCallbackAlive()
			{
				// To nothing.
			}
		}
	}
}
