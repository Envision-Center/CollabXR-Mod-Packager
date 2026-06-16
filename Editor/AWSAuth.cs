using System;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.CognitoIdentity;
using Amazon.CognitoIdentityProvider;
using Amazon.Extensions.CognitoAuthentication;
using Amazon.Runtime;
using Amazon.S3;

namespace CollabXR.ModPackager
{
	public class AWSAuth
	{
		string userPool;
		string identityPool;
		string clientID;

		public bool IsAuthenticated
		{
			get { return user != null; }
		}

		public RegionEndpoint targetRegionEndpoint { get; private set; } = RegionEndpoint.USEast1;

		public CognitoAWSCredentials credentials { get; private set; }
		private CognitoUser user;

		CancellationTokenSource source = new CancellationTokenSource();

		public delegate void OnSignInSuccessEvent();
		public event OnSignInSuccessEvent OnSignInSuccess;

		public delegate void SignInFailedEvent();
		public event SignInFailedEvent OnSignInFailed;

		public delegate void SignOutEvent();
		public event SignOutEvent OnSignOut;

		public delegate string NewPasswordChallengeEvent();
		public event NewPasswordChallengeEvent OnNewPasswordChallenge;

		public delegate string MFAChallengeEvent();
		public event MFAChallengeEvent OnMFAChallenge;

		public AWSAuth(RepositoryMetadata repositoryMetadata)
		{
			userPool = repositoryMetadata.CognitoUserPool;
			identityPool = repositoryMetadata.CognitoIdentityPool;
			clientID = repositoryMetadata.CognitoClientID;

			Logger.VerboseInfo($"AWSAuth created, using userPool {userPool}, identityPool {identityPool}, clientID {clientID}");
		}

		public async Task SignIn(string username, string password)
		{
			source.Cancel();
			source.Dispose();

			source = new CancellationTokenSource();

			Logger.VerboseInfo("Signing in...");

			Logger.VerboseInfo("Registering provider...");

			AmazonCognitoIdentityProviderClient provider = new AmazonCognitoIdentityProviderClient(new AnonymousAWSCredentials(), targetRegionEndpoint);

			Logger.VerboseInfo("Registering User Pool...");

			CognitoUserPool cognitoUserPool = new CognitoUserPool(userPool, clientID, provider);

			Logger.VerboseInfo("Registering User...");

			user = new CognitoUser(username, clientID, cognitoUserPool, provider);

			Logger.VerboseInfo("Creating SRP Auth Flow...");

			InitiateSrpAuthRequest authRequest = new InitiateSrpAuthRequest() { Password = password };

			AuthFlowResponse authResponse;

			try
			{
				Logger.VerboseInfo("Sending SRP Auth Flow...");

				authResponse = await user.StartWithSrpAuthAsync(authRequest).ConfigureAwait(false);
			}
			catch (Exception e)
			{
				Logger.VerboseError("SRP Auth Flow Failed:");
				Logger.VerboseError(e);

				OnSignInFailed.Invoke();

				return;
			}

			while (authResponse.AuthenticationResult == null)
			{
				if (authResponse.ChallengeName == ChallengeNameType.NEW_PASSWORD_REQUIRED)
				{
					Logger.VerboseInfo("Cognito presented New Password Challenge...");

					string newPassword = OnNewPasswordChallenge.Invoke();

					Logger.VerboseInfo("Responding to New Password Challenge...");

					authResponse = await user.RespondToNewPasswordRequiredAsync(new RespondToNewPasswordRequiredRequest() { SessionID = authResponse.SessionID, NewPassword = newPassword });
				}
				else if (authResponse.ChallengeName == ChallengeNameType.SMS_MFA)
				{
					Logger.VerboseInfo("Cognito presented MFA Challenge...");

					string mfaCode = OnMFAChallenge.Invoke();

					Logger.VerboseInfo("Responding to MFA Challenge...");

					authResponse = await user.RespondToSmsMfaAuthAsync(new RespondToSmsMfaRequest() { SessionID = authResponse.SessionID, MfaCode = mfaCode }).ConfigureAwait(false);
				}
				else
				{
					Logger.VerboseError("SRP Auth Flow Challenges Failed");
					OnSignInFailed.Invoke();

					return;
				}
			}

			if (authResponse.AuthenticationResult == null)
			{
				Logger.VerboseError("SRP Auth Flow Challenges Failed");
				OnSignInFailed.Invoke();

				return;
			}

			Logger.VerboseInfo("Acquiring session token...");

			credentials = user.GetCognitoAWSCredentials(identityPool, targetRegionEndpoint);

			Logger.VerboseInfo("Adding session token...");

			credentials.AddLogin($"cognito-idp.us-east-1.amazonaws.com/{this.userPool}", authResponse.AuthenticationResult.IdToken);

			OnSignInSuccess.Invoke();

			Logger.VerboseInfo("Setting expiration...");

			_ = Task.Delay(TimeSpan.FromMinutes(45), source.Token).ContinueWith((task) => SignOut());

			Logger.VerboseInfo("Signed in");
		}

		public void SignOut()
		{
			Logger.VerboseInfo($"Signing out...");

			OnSignOut.Invoke();

			if (user != null)
			{
				user.SignOut();
			}

			Logger.VerboseInfo($"Signed out");
		}
	}
}
