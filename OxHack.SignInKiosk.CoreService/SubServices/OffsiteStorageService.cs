﻿using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using NLog;
using OxHack.SignInKiosk.Database.Services;
using OxHack.SignInKiosk.Domain.Models;
using OxHack.SignInKiosk.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IO = System.IO;

namespace OxHack.SignInKiosk.CoreService
{
	class OffsiteStorageService
	{
		private readonly ILogger logger = LogManager.GetCurrentClassLogger();

		private readonly SignInService signInService;
		private readonly MessagingClient messagingClient;
		private readonly string fileId;

		private DriveService driveService;

		public OffsiteStorageService(SignInService signInService, MessagingClient messagingClient)
		{
			this.signInService = signInService;
			this.messagingClient = messagingClient;
			this.messagingClient.PersonSignedIn += (s, e) => this.Update();
			this.messagingClient.PersonSignedOut += (s, e) => this.Update();

			this.fileId = Environment.GetEnvironmentVariable("GoogleDriveFileId");
		}

		public async Task Connect()
		{
			var serviceAccountEmail = Environment.GetEnvironmentVariable("GoogleDriveServiceAccountEmail");
			var serviceAccountPrivateKey = Environment.GetEnvironmentVariable("GoogleDriveServiceAccountPrivateKey").Replace("\\n", "\n");

			ServiceAccountCredential serviceCredential =
				new ServiceAccountCredential(
					new ServiceAccountCredential.Initializer(serviceAccountEmail)
					{
						Scopes = new[] { DriveService.Scope.Drive },
					}.FromPrivateKey(serviceAccountPrivateKey));

			this.driveService = new DriveService(new BaseClientService.Initializer()
			{
				HttpClientInitializer = serviceCredential,
				ApplicationName = "OxHack Sign-In Kiosk",
			});
		}

		public async Task Disconnect()
		{
			await Task.FromResult(0);
		}

		private void Update()
		{
			var currentState = this.signInService.GetCurrentlySignedIn();
			var past24Hours = this.signInService.GetPast24Hours().OrderByDescending(item => item.Time).ToList();
			var lastUpdateTime = DateTime.Now;

			string content = FormatReport(currentState, past24Hours, lastUpdateTime);

			var file = new File();

			using (var stream = new IO.MemoryStream(Encoding.UTF8.GetBytes(content)))
			{
				var request = this.driveService.Files.Update(file, this.fileId, stream, "text/plain");
				var upload = request.Upload();
			}
		}

		private static string FormatReport(IReadOnlyList<SignedInRecord> currentState, List<AuditRecord> past24Hours, DateTime lastUpdateTime)
		{
			return $@"Last Updated: {lastUpdateTime.ToString("s").Replace('T', ' ')}

Currently Signed-In:
====================

Sign-In Time          Is Visitor?  Name
--------------------  -----------  --------------------
{String.Join("\r\n",
				currentState
					.Select(item =>
						item.SignInTime.ToString("s").Replace('T', ' ').PadRight(22) +
						(item.IsVisitor ? "Yes" : String.Empty).PadRight(13) +
						item.DisplayName
						)
					.DefaultIfEmpty("    * ** ***  Nobody is in the hackspace.  *** ** *    ")
			)}


















Past 24 Hours:
==============

Event Time            Event       Is Visitor?  Name
--------------------  ----------  -----------  --------------------
{String.Join("\r\n", past24Hours.Select(item =>
				item.Time.ToString("s").Replace('T', ' ').PadRight(22) +
				item.RecordType.PadRight(12) +
				(item.PersonIsVisitor ? "Yes" : String.Empty).PadRight(13) +
				item.PersonDisplayName
				))}
";
		}
	}
}