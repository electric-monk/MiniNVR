using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ServiceModel.Description;
using System.ServiceModel.Channels;
using System.ServiceModel.Dispatcher;
using System.Xml;
using System.Security.Cryptography;
using System.ServiceModel;

namespace TestConsole
{
    class UsernameTokenEndpointBehaviour : IEndpointBehavior
    {
        private readonly ClientInspector inspector;

        public UsernameTokenEndpointBehaviour(string username, string password)
        {
            inspector = new ClientInspector(new SecurityHeader(username, password));
        }

        public void AddBindingParameters(ServiceEndpoint endpoint, BindingParameterCollection bindingParameters)
        {
        }

        public void ApplyClientBehavior(ServiceEndpoint endpoint, ClientRuntime clientRuntime)
        {
            clientRuntime.ClientMessageInspectors.Add(inspector);
        }

        public void ApplyDispatchBehavior(ServiceEndpoint endpoint, EndpointDispatcher endpointDispatcher)
        {
        }

        public void Validate(ServiceEndpoint endpoint)
        {
        }

        private class ClientInspector : IClientMessageInspector
        {
            private readonly MessageHeader header;

            public ClientInspector(MessageHeader h)
            {
                header = h;
            }

            object IClientMessageInspector.BeforeSendRequest(ref Message request, IClientChannel channel)
            {
                request.Headers.Insert(0, header);
                return request;
            }

            void IClientMessageInspector.AfterReceiveReply(ref Message reply, object correlationState)
            {
            }
        }

        private class SecurityHeader : MessageHeader
        {
            private readonly string username;
            private readonly string password;

            public SecurityHeader(string u, string p)
            {
                username = u;
                password = p;
            }

            public override string Name
            {
                get
                {
                    return "Security";
                }
            }

            public override string Namespace
            {
                get
                {
                    return "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
                }
            }

            protected override void OnWriteHeaderContents(XmlDictionaryWriter writer, MessageVersion messageVersion)
            {
                byte[] nonce = new byte[64];
                RandomNumberGenerator.Create().GetBytes(nonce);
                string created = DateTime.Now.ToString("yyyy-MM-ddThh:mm:ssZ");
                writer.WriteStartElement("wsse", "UsernameToken", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd");
                writer.WriteXmlnsAttribute("wsu", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd");
                writer.WriteAttributeString("wsu", "Id", null, "User");
                writer.WriteStartElement("wsse", "Username", null);
                writer.WriteString(username);
                writer.WriteEndElement();
                writer.WriteStartElement("wsse", "Password", null);
                writer.WriteAttributeString("Type", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest");
                writer.WriteString(ComputeDigest(password, nonce, created));
                writer.WriteEndElement();
                writer.WriteStartElement("wsse", "Nonce", null);
                writer.WriteAttributeString("EncodingType", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary");
                writer.WriteBase64(nonce, 0, nonce.Length);
                writer.WriteEndElement();
                writer.WriteStartElement("wsu", "Created", null);
                writer.WriteString(created);
                writer.WriteEndElement();
                writer.WriteEndElement();
                writer.Flush();
            }

            private string ComputeDigest(string secret, byte[] nonceInBytes, string created)
            {
                byte[] createdInBytes = Encoding.UTF8.GetBytes(created);
                byte[] secretInBytes = Encoding.UTF8.GetBytes(secret);
                byte[] concatenation = new byte[nonceInBytes.Length + createdInBytes.Length + secretInBytes.Length];
                Array.Copy(nonceInBytes, concatenation, nonceInBytes.Length);
                Array.Copy(createdInBytes, 0, concatenation, nonceInBytes.Length, createdInBytes.Length);
                Array.Copy(secretInBytes, 0, concatenation, nonceInBytes.Length + createdInBytes.Length, secretInBytes.Length);
                return Convert.ToBase64String(SHA1.Create().ComputeHash(concatenation));
            }
        }
    }
}
