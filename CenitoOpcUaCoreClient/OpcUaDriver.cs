
using log4net;
using log4net.Config;
using Opc.Ua;
using Opc.Ua.Client;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OpcUa
{
    public class OpcUaDriver 
    {
        private const int NumberOfFailedKeepingAliveEventsBeforeRestart = 5;
        private bool m_connecting = false;
        private string m_EndpointURL;
        private readonly ILog m_Log = LogManager.GetLogger(typeof(OpcUaDriver));
        private string m_ServerEndPointUrl;
        private Session m_Session;
        private Subscription m_Subscription;
        private List<MonitoredItem> m_MonitoredItems;
        private IEnumerable<string> m_TagInfos;
        private ServerState m_PreviousState;
      
        int m_FailedKeepAliveEvents;
        int m_NumberOfCommunicationErrors;
        

        public string ServerEndPointUrl
        {
            get { return m_ServerEndPointUrl; }
            set { m_ServerEndPointUrl = value; }
        }

        public bool IsConnected
        {
            get { return m_Session != null; }
        }

        public IDictionary<string, string> KeysToValueDictionary { get; private set; }
        

        public OpcUaDriver()
        {
            XmlConfigurator.Configure(LogManager.GetAllRepositories()[0], new FileInfo("log4net.config"));
            KeysToValueDictionary = new Dictionary<string, string>();
            m_MonitoredItems = new List<MonitoredItem>();           
        }

      
        public async Task InitializeClient(string endpointURL, IEnumerable<string> tagInfos)
        {
            m_NumberOfCommunicationErrors = 0;

            m_PreviousState = ServerState.Unknown;
          
            m_FailedKeepAliveEvents = 0;
            m_EndpointURL = endpointURL;
            m_TagInfos = tagInfos;
            var config = await CreateApplicationConfiguration();
            m_connecting = true;
            int numberOfTries = 0;

            while (m_connecting)
            {
                try
                {
                     numberOfTries++;
                     
                    bool haveAppCertificate = ValidateCertificate(config);
                    m_Session =  await CreateSession(config, endpointURL, haveAppCertificate);

                    m_connecting = !m_Session.Connected;

                    if (m_Session.Connected)
                    {
                        m_Session.KeepAlive += OnSessionKeepAlive;
                    }                

                }
                catch (Exception ex)
                {
                    m_Log.Error($"Failed to connect to OPC Server {endpointURL}", ex);
                    await Task.Delay(1000);
                }
            }
      
            Console.WriteLine("4 - Create a subscription with publishing interval of 1 second.");
            m_Subscription = new Subscription(m_Session.DefaultSubscription) { PublishingInterval = 1000 };


            foreach (var tagInfo in m_TagInfos)
            {
                if (string.IsNullOrEmpty(tagInfo))
                    continue;

                var monitoredItem = new MonitoredItem(m_Subscription.DefaultItem)
                {
                    DisplayName = tagInfo,
                    StartNodeId = new NodeId(tagInfo, 6),
                    Handle = tagInfo
                };

                m_MonitoredItems.Add(monitoredItem);
            }

            m_MonitoredItems.ForEach(i => i.Notification += OnNotification);
            m_Subscription.AddItems(m_MonitoredItems);

            Console.WriteLine("6 - Add the subscription to the session.");
            m_Session.AddSubscription(m_Subscription);
            m_Subscription.Create();   
        }

        private void OnSessionKeepAlive(Session session, KeepAliveEventArgs e)
        {
            var status = string.Empty;
            
            if (e.Status != null && e.Status.StatusCode!= null)
            {
                status = e.Status.StatusCode.ToString();
            }
            if (e.CurrentState != ServerState.Running)
            {
                m_FailedKeepAliveEvents++;
                m_NumberOfCommunicationErrors++;

                m_Log.Error(status);
             
            }
            else if (m_PreviousState != ServerState.Running && e.CurrentState == ServerState.Running)
            {
                m_Log.Error($"OPC is running again, after state: '{m_PreviousState}'.");               
        
                m_FailedKeepAliveEvents = 0;
            }

            if (m_FailedKeepAliveEvents == NumberOfFailedKeepingAliveEventsBeforeRestart)
            {
                m_FailedKeepAliveEvents = 0;

                m_Log.Error($"Restarting OPC server due to keep alive failures.");
            
                ReConnectToServer();
            }          

            m_PreviousState = e.CurrentState;
        }

        private  async Task<Session> CreateSession(ApplicationConfiguration config, string endpointURL, bool haveAppCertificate)
        {
            EndpointDescription endpointDescription = SelectEndpoint(endpointURL, haveAppCertificate);

            EndpointConfiguration endpointConfiguration = EndpointConfiguration.Create(config);
            ConfiguredEndpoint endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

            return await Session.Create(
                config,
                endpoint,
                false,
                true,
               "Gatway OPC UA Session",
                60000,
                null,
                null);
        }

        private bool ValidateCertificate(ApplicationConfiguration config)
        {
            bool haveAppCertificate = config.SecurityConfiguration.ApplicationCertificate.Certificate != null;

            if (config.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                config.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }

            if (!haveAppCertificate)
            {
                Debug.WriteLine("    WARN: missing application certificate, using unsecure connection.");
            }
            return haveAppCertificate;
        }
        private async Task<ApplicationConfiguration> CreateApplicationConfiguration()
        {
            var config = new ApplicationConfiguration()
            {
                ApplicationName = "Gateway Client",
                ApplicationType = ApplicationType.Client,
                ApplicationUri = "urn:localhost:OpcUa:OpCUdiver",
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = "Directory",
                        StorePath = "./OPC Foundation/CertificateStores/MachineDefault",
                        SubjectName = Utils.Format("CN={0}, DC={1}", "UA Sample Client", Utils.GetHostName())
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = "Directory",
                        StorePath = "./OPC Foundation/CertificateStores/UA Applications",
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = "Directory",
                        StorePath = "./OPC Foundation/CertificateStores/UA Certificate Authorities",
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = "Directory",
                        StorePath = "./OPC Foundation/CertificateStores/RejectedCertificates",
                    },
                    NonceLength = 32,
                    AutoAcceptUntrustedCertificates = true
                },
                TransportConfigurations = new TransportConfigurationCollection(),
                TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
                ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 }
            };

            await config.Validate(ApplicationType.Client);

            return config;
        }
        private void OnNotification(MonitoredItem item, MonitoredItemNotificationEventArgs e)
        {
            foreach (var value in item.DequeueValues())
            {
              
                Console.WriteLine("{0}: {1}, {2}, {3}", item.DisplayName, value.Value, DateTime.UtcNow, value.StatusCode);
              
            }

        }

        private static void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e)
        {
            Console.WriteLine("Accepted Certificate: {0}", e.Certificate.Subject);
            e.Accept = (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted);
        }

        private static EndpointDescriptionCollection DiscoverEndpoints(ApplicationConfiguration config, Uri discoveryUrl, int timeout)
        {
            // use a short timeout.
            EndpointConfiguration configuration = EndpointConfiguration.Create(config);
            configuration.OperationTimeout = timeout;

            using (DiscoveryClient client = DiscoveryClient.Create(
                discoveryUrl,
                EndpointConfiguration.Create(config)))
            {
                try
                {
                    EndpointDescriptionCollection endpoints = client.GetEndpoints(null);
                    ReplaceLocalHostWithRemoteHost(endpoints, discoveryUrl);
                    return endpoints;
                }
                catch (Exception e)
                {
                    Console.WriteLine("Could not fetch endpoints from url: {0}", discoveryUrl);
                    Console.WriteLine("Reason = {0}", e.Message);
                    throw e;
                }
            }
        }
        
        private static void ReplaceLocalHostWithRemoteHost(EndpointDescriptionCollection endpoints, Uri discoveryUrl)
        {
            foreach (EndpointDescription endpoint in endpoints)
            {
                endpoint.EndpointUrl = Utils.ReplaceLocalhost(endpoint.EndpointUrl, discoveryUrl.DnsSafeHost);
                StringCollection updatedDiscoveryUrls = new StringCollection();
                foreach (string url in endpoint.Server.DiscoveryUrls)
                {
                    updatedDiscoveryUrls.Add(Utils.ReplaceLocalhost(url, discoveryUrl.DnsSafeHost));
                }
                endpoint.Server.DiscoveryUrls = updatedDiscoveryUrls;
            }
        }

        private void ReConnectToServer()
        {
            DisconnectFromClient();
#pragma warning disable
            InitializeClient(m_EndpointURL, m_TagInfos);
        }

        private void DisconnectFromClient()
        {
            try
            {                
                m_connecting = false;
                m_MonitoredItems.ForEach(i => i.Notification -= OnNotification);
                if (m_Subscription != null)
                {
                    m_Subscription.RemoveItems(m_MonitoredItems);
                    m_Subscription.Dispose();
                }
                if (m_Session != null)
                {
                    m_Session.KeepAlive -= OnSessionKeepAlive;                    
                    m_Session.Dispose();
                }
                m_Session = null;
                m_Subscription = null;
                m_MonitoredItems.Clear();

            }
            catch (Exception ex)
            {
                m_Log.Debug("Failed to disconnect OPCUA driver", ex);
            }
        }
        public void Dispose()
        {
            DisconnectFromClient();
        }

        public EndpointDescription SelectEndpoint(string discoveryUrl, bool useSecurity)
        {
            // needs to add the '/discovery' back onto non-UA TCP URLs.
            if (!discoveryUrl.StartsWith(Utils.UriSchemeOpcTcp))
            {
                if (!discoveryUrl.EndsWith("/discovery"))
                {
                    discoveryUrl += "/discovery";
                }
            }

            // parse the selected URL.
            Uri uri = new Uri(discoveryUrl);

            // set a short timeout because this is happening in the drop down event.
            EndpointConfiguration configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 5000;

            EndpointDescription selectedEndpoint = null;

            // Connect to the server's discovery endpoint and find the available configuration.
            using (DiscoveryClient client = DiscoveryClient.Create(uri, configuration))
            {
                EndpointDescriptionCollection endpoints = client.GetEndpoints(null);

                // select the best endpoint to use based on the selected URL and the UseSecurity checkbox. 
                for (int ii = 0; ii < endpoints.Count; ii++)
                {
                    EndpointDescription endpoint = endpoints[ii];

                    // check for a match on the URL scheme.
                    if (endpoint.EndpointUrl.StartsWith(uri.Scheme))
                    {
                        // check if security was requested.
                        if (useSecurity)
                        {
                            if (endpoint.SecurityMode == MessageSecurityMode.None)
                            {
                                continue;
                            }
                        }
                        else
                        {
                            if (endpoint.SecurityMode != MessageSecurityMode.None)
                            {
                                continue;
                            }
                        }

                        // pick the first available endpoint by default.
                        if (selectedEndpoint == null)
                        {
                            selectedEndpoint = endpoint;
                        }

                        // The security level is a relative measure assigned by the server to the 
                        // endpoints that it returns. Clients should always pick the highest level
                        // unless they have a reason not too.
                        if (endpoint.SecurityLevel > selectedEndpoint.SecurityLevel)
                        {
                            selectedEndpoint = endpoint;
                        }
                    }
                }

                // pick the first available endpoint by default.
                if (selectedEndpoint == null && endpoints.Count > 0)
                {
                    selectedEndpoint = endpoints[0];
                }
            }

            // if a server is behind a firewall it may return URLs that are not accessible to the client.
            // This problem can be avoided by assuming that the domain in the URL used to call 
            // GetEndpoints can be used to access any of the endpoints. This code makes that conversion.
            // Note that the conversion only makes sense if discovery uses the same protocol as the endpoint.

            Uri endpointUrl = Utils.ParseUri(selectedEndpoint.EndpointUrl);

            if (endpointUrl != null && endpointUrl.Scheme == uri.Scheme)
            {
                UriBuilder builder = new UriBuilder(endpointUrl);
                builder.Host = uri.DnsSafeHost;
                builder.Port = uri.Port;
                selectedEndpoint.EndpointUrl = builder.ToString();
            }

            // return the selected endpoint.
            return selectedEndpoint;
        }
    }
}

