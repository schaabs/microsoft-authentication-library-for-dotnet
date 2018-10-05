﻿//----------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.
// All rights reserved.
//
// This code is licensed under the MIT License.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files(the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions :
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
//------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Identity.Core;
using Microsoft.Identity.Core.WsTrust;
using Microsoft.IdentityModel.Clients.ActiveDirectory.Internal.Instance;
using Microsoft.IdentityModel.Clients.ActiveDirectory.Internal.OAuth2;

namespace Microsoft.IdentityModel.Clients.ActiveDirectory.Internal.Flows
{
    /// <summary>
    /// Handler for Integrated Windows Authentication
    /// </summary>
    internal class AcquireTokenIWAHandler : AcquireTokenHandlerBase
    {
        private IntegratedWindowsAuthInput iwaInput;
        private UserAssertion userAssertion;
        private CommonNonInteractiveHandler commonNonInteractiveHandler;

        public AcquireTokenIWAHandler(RequestData requestData, IntegratedWindowsAuthInput iwaInput)
            : base(requestData)
        {
            if (iwaInput == null)
            {
                throw new ArgumentNullException(nameof(iwaInput));
            }

            // We enable ADFS support only when it makes sense to do so
            if (requestData.Authenticator.AuthorityType == AuthorityType.ADFS)
            {
                SupportADFS = true;
            }

            this.iwaInput = iwaInput;
            DisplayableId = iwaInput.UserName;

            commonNonInteractiveHandler = new CommonNonInteractiveHandler(RequestContext, this.iwaInput);
        }

        protected override async Task PreRunAsync()
        {
            await base.PreRunAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(iwaInput.UserName))
            {
                string platformUsername = await commonNonInteractiveHandler.GetPlatformUserAsync()
                    .ConfigureAwait(false);
                iwaInput.UserName = platformUsername;
            }

            DisplayableId = iwaInput.UserName;
        }

        protected override async Task PreTokenRequestAsync()
        {
            await base.PreTokenRequestAsync().ConfigureAwait(false);

            if (!SupportADFS)
            {
                var userRealmResponse = await commonNonInteractiveHandler.QueryUserRealmDataAsync(Authenticator.UserRealmUriPrefix)
                   .ConfigureAwait(false);

                if (string.Equals(userRealmResponse.AccountType, "federated", StringComparison.OrdinalIgnoreCase))
                {
                    WsTrustResponse wsTrustResponse = await commonNonInteractiveHandler.PerformWsTrustMexExchangeAsync(
                        userRealmResponse.FederationMetadataUrl,
                        userRealmResponse.CloudAudienceUrn,
                        UserAuthType.IntegratedAuth).ConfigureAwait(false);

                    // We assume that if the response token type is not SAML 1.1, it is SAML 2
                    userAssertion = new UserAssertion(wsTrustResponse.Token, (wsTrustResponse.TokenType == WsTrustResponse.Saml1Assertion) ? OAuthGrantType.Saml11Bearer : OAuthGrantType.Saml20Bearer);
                }
                else
                {
                    throw new AdalException(AdalError.UnknownUserType);
                }
            }
        }

        protected override void AddAditionalRequestParameters(DictionaryRequestParameters requestParameters)
        {
            Debug.Assert(userAssertion != null, "Expected the user assertion to have been created by PreTokenRequestAsync");

            requestParameters[OAuthParameter.GrantType] = userAssertion.AssertionType;
            requestParameters[OAuthParameter.Assertion] = Convert.ToBase64String(Encoding.UTF8.GetBytes(userAssertion.Assertion));

            // To request id_token in response
            requestParameters[OAuthParameter.Scope] = OAuthValue.ScopeOpenId;
        }
    }
}
