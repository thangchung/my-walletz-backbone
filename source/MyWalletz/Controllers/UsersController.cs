﻿namespace MyWalletz.Controllers
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using System.Web;
    using System.Web.Http;
    using System.Web.Security;

    using Infrastructure;
    using Models;

    public class UsersController : ApiController
    {
        private readonly Func<string, string, bool, string> signup;
        private readonly IMailer mailer;
        private readonly IUrlSafeSecureDataSerializer urlSafeSecureDataSerializer;
        private readonly ISeedDataLoader seedDataLoader;

        private bool? debuggingEnabled;

        public UsersController(
            Func<string, string, bool, string> signup,
            IMailer mailer,
            IUrlSafeSecureDataSerializer urlSafeSecureDataSerializer,
            ISeedDataLoader seedDataLoader)
        {
            this.signup = signup;
            this.mailer = mailer;
            this.urlSafeSecureDataSerializer = urlSafeSecureDataSerializer;
            this.seedDataLoader = seedDataLoader;
        }

        public bool IsDebuggingEnabled
        {
            get
            {
                if (debuggingEnabled == null)
                {
                    object context;

                    if (Request.Properties.TryGetValue("MS_HttpContext", out context))
                    {
                        var httpContext = context as HttpContextBase;

                        debuggingEnabled = (httpContext != null) && httpContext.IsDebuggingEnabled;
                    }
                    else
                    {
                        debuggingEnabled = false;
                    }
                }

                return debuggingEnabled.GetValueOrDefault();
            }

            set
            {
                debuggingEnabled = value;
            }
        }

        public async Task<HttpResponseMessage> Post(CreateUser model)
        {
            if (!ModelState.IsValid)
            {
                return Request.CreateErrorResponse(
                    HttpStatusCode.BadRequest, ModelState);
            }

            var statusCode = MembershipCreateStatus.Success;
            var userName = model.Email.ToLowerInvariant();
            var token = string.Empty;

            var requireConfirmation = !IsDebuggingEnabled;

            try
            {
                token = signup(userName, model.Password, requireConfirmation);
            }
            catch (MembershipCreateUserException e)
            {
                statusCode = e.StatusCode;
            }

            if (statusCode == MembershipCreateStatus.Success)
            {
                if (requireConfirmation)
                {
                    var userConfirmationToken = new UserConfirmationToken
                                                    {
                                                        Email = userName,
                                                        Token = token
                                                    };

                    var securedToken = urlSafeSecureDataSerializer.Serialize(
                        userConfirmationToken);

                    await mailer.UserConfirmationAsync(userName, securedToken);
                }
                else
                {
                    seedDataLoader.Load(userName);
                }

                return Request.CreateResponse(HttpStatusCode.NoContent);
            }

            switch (statusCode)
            {
                case MembershipCreateStatus.DuplicateUserName:
                case MembershipCreateStatus.DuplicateEmail:
                case MembershipCreateStatus.DuplicateProviderUserKey:
                    ModelState.AddModelError(
                        "email",
                        "User with same email already exits.");
                    break;
                case MembershipCreateStatus.InvalidUserName:
                case MembershipCreateStatus.InvalidEmail:
                    ModelState.AddModelError(
                        "email",
                        "Invalid email address.");
                    break;
                case MembershipCreateStatus.InvalidPassword:
                    ModelState.AddModelError("password", "Invalid password.");
                    break;
                default:
                    ModelState.AddModelError(
                        string.Empty,
                        "Unexpected error.");
                    break;
            }

            return Request.CreateErrorResponse(
                HttpStatusCode.BadRequest, ModelState);
        }
    }
}