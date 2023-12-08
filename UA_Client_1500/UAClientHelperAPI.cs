﻿//=============================================================================
// Siemens AG
// (c)Copyright (2022) All Rights Reserved
//----------------------------------------------------------------------------- 
// Tested with: Windows 10 Enterprise x64
// Engineering: Visual Studio 2022
// Functionality: Wrapps up important classes/methods of the OPC UA .NET Stack (Core/Client) to help
// with simple client implementations
//-----------------------------------------------------------------------------
// Changes: see CHANGELOG.md
//=============================================================================

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Linq;
using Opc.Ua;
using Opc.Ua.Client;

namespace Siemens.UAClientHelper
{
    public class UAClientHelperAPI
    {
        #region Construction
        public UAClientHelperAPI()
        {
            // Creats the application configuration (containing the certificate) on construction
            mApplicationConfig = CreateClientConfiguration();
        }
        #endregion

        #region Properties
        /// <summary> 
        /// Keeps a session with an UA server.
        /// </summary>
        private Session mSession = null;

        /// <summary> 
        /// Specifies this application.
        /// </summary>
        private ApplicationConfiguration mApplicationConfig = null;

        /// <summary>
        /// Provides the session being established with an OPC UA server.
        /// </summary>
        public Session Session
        {
            get { return mSession; }
        }
        /// <summary>
        /// The number of seconds between reconnect attempts (0 means reconnect is disabled)
        /// </summary>
        /// 
        public int ReconnectPeriod { get; set; } = 10;

        /// <summary>
        /// Provides the client certificate.
        /// </summary>
        /// 
        public X509Certificate2 clientCertificate = null;

        /// <summary>
        /// Provides the event handling for server certificates.
        /// </summary>
        public CertificateValidationEventHandler CertificateValidationNotification = null;

        /// <summary>
        /// Provides the event for value changes of a monitored item.
        /// </summary>
        public MonitoredItemNotificationEventHandler ItemChangedNotification = null;

        /// <summary>
        /// Provides the event for a monitored event item.
        /// </summary>
        public NotificationEventHandler ItemEventNotification = null;

        /// <summary>
        /// Provides the event for KeepAliveNotifications.
        /// </summary>
        public KeepAliveEventHandler KeepAliveNotification = null;
        #endregion

        #region Discovery
        /// <summary>Finds Servers based on a discovery url</summary>
        /// <param name="discoveryUrl">The discovery url</param>
        /// <returns>ApplicationDescriptionCollection containing found servers</returns>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public ApplicationDescriptionCollection FindServers(string discoveryUrl)
        {
            //Create a URI using the discovery URL
            Uri uri = new Uri(discoveryUrl);
            //Ceate a DiscoveryClient
            DiscoveryClient client = DiscoveryClient.Create(uri);
            //Find servers
            //ApplicationDescriptionCollection servers = client.FindServers(null);
            ApplicationDescriptionCollection servers = client.FindServers(null);
            client.Close();
            client.Dispose();
            return servers;
        }

        /// <summary>Finds Endpoints based on a server's url</summary>
        /// <param name="discoveryUrl">The server's url</param>
        /// <returns>EndpointDescriptionCollection containing found Endpoints</returns>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public EndpointDescriptionCollection GetEndpoints(string serverUrl)
        {
            //Create a URI using the server's URL
            Uri uri = new Uri(serverUrl);
            //Create a DiscoveryClient
            DiscoveryClient client = DiscoveryClient.Create(uri);
            //Search for available endpoints
            EndpointDescriptionCollection endpoints = client.GetEndpoints(null);
            client.Close();
            client.Dispose();
            return endpoints;
        }
        #endregion

        #region Connect/Disconnect
        /// <summary>Establishes the connection to an OPC UA server and creates a session using an EndpointDescription.</summary>
        /// <param name="endpointDescription">The EndpointDescription of the server's endpoint</param>
        /// <param name="userAuth">Autheticate anonymous or with username and password</param>
        /// <param name="userName">The user name</param>
        /// <param name="password">The password</param>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public async Task Connect(EndpointDescription endpointDescription, bool userAuth, string userName, string password)
        {
            //Secify application configuration
            ApplicationConfiguration ApplicationConfig = mApplicationConfig;

            //Get client certificate
            clientCertificate = await ApplicationConfig.SecurityConfiguration.ApplicationCertificate.Find(true).ConfigureAwait(false);

            //Hook up a validator function for a CertificateValidation event
            mApplicationConfig.CertificateValidator.CertificateValidation += Notification_CertificateValidation;

            //Create EndPoint configuration
            EndpointConfiguration EndpointConfiguration = EndpointConfiguration.Create(ApplicationConfig);

            //Connect to server and get endpoints
            ConfiguredEndpoint mEndpoint = new ConfiguredEndpoint(null, endpointDescription, EndpointConfiguration);

            //Create the binding factory.
            //BindingFactory bindingFactory = BindingFactory.Create(mApplicationConfig, ServiceMessageContext.GlobalContext);

            //Creat a session name
            String sessionName = "MySession";

            //Create user identity
            UserIdentity UserIdentity;
            if (userAuth)
            {
                UserIdentity = new UserIdentity(userName, password);
            }
            else
            {
                UserIdentity = new UserIdentity();
            }

            //Update certificate store before connection attempt
            await ApplicationConfig.CertificateValidator.Update(ApplicationConfig);

            //Create and connect session

            mSession = await Session.Create(
                ApplicationConfig,
                mEndpoint,
                true,
                sessionName,
                60000,
                UserIdentity,
                null
                );

            //mSession.KeepAlive += new KeepAliveEventHandler(Notification_KeepAlive);
            mSession.KeepAlive += new KeepAliveEventHandler(Notification_KeepAlive);
        }

        /// <summary>Closes an existing session and disconnects from the server.</summary>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public void Disconnect()
        {
            // Close the session.
            if (mSession != null)
            {
                mSession.Close(10000);
                mSession.Dispose();
            }
        }
        #endregion

        #region Namspace
        /// <summary>Returns the namespace uri at the specified index.</summary>
        /// <param name="index">the namespace index</param>
        /// <returns>The namespace uri</returns>
        public String GetNamespaceUri(uint index)
        {
            //Check the length of namespace array
            if (mSession.NamespaceUris.Count > index)
            {   //Get the uri for the namespace index
                return mSession.NamespaceUris.GetString(index);
            }
            else
            {
                Exception e = new Exception("Index is out of range");
                throw e;
            }
        }

        /// <summary>Returns the index of the specified namespace uri.</summary>
        /// <param name="uri">The namespace uri</param>
        /// <returns>The namespace index</returns>
        public uint GetNamespaceIndex(String uri)
        {
            //Get the namespace index of the specified namespace uri
            int namespaceIndex = mSession.NamespaceUris.GetIndex(uri);
            //If the namespace uri doesn't exist, namespace index is -1 
            if (namespaceIndex >= 0)
            {
                return (uint)namespaceIndex;
            }
            else
            {
                Exception e = new Exception("Namespace doesn't exist");
                throw e;
            }
        }

        /// <summary>Returns a list of all namespace uris.</summary>
        /// <returns>The name space array</returns>
        public List<String> GetNamespaceArray()
        {
            List<String> namespaceArray = new List<String>();

            //Read all namespace uris and add the uri to the list
            for (uint i = 0; i < mSession.NamespaceUris.Count; i++)
            {
                namespaceArray.Add(mSession.NamespaceUris.GetString(i));
            }

            return namespaceArray;
        }
        #endregion

        #region Browse
        /// <summary>Browses the root folder of an OPC UA server.</summary>
        /// <returns>ReferenceDescriptionCollection of found nodes</returns>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public ReferenceDescriptionCollection BrowseRoot()
        {
            //Create a collection for the browse results
            ReferenceDescriptionCollection referenceDescriptionCollection;
            //Create a continuationPoint
            byte[] continuationPoint;
            //Browse the RootFolder for variables, objects and methods
            mSession.Browse(null, null, ObjectIds.RootFolder, 0u, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences, true, (uint)NodeClass.Variable | (uint)NodeClass.Object | (uint)NodeClass.Method, out continuationPoint, out referenceDescriptionCollection);
            return referenceDescriptionCollection;
        }

        /// <summary>Browses a node ID provided by a ReferenceDescription</summary>
        /// <param name="refDesc">The ReferenceDescription</param>
        /// <returns>ReferenceDescriptionCollection of found nodes</returns>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public ReferenceDescriptionCollection BrowseNode(ReferenceDescription refDesc)
        {
            //Create a collection for the browse results
            ReferenceDescriptionCollection referenceDescriptionCollection;
            ReferenceDescriptionCollection nextreferenceDescriptionCollection;
            //Create a continuationPoint
            byte[] continuationPoint;
            byte[] revisedContinuationPoint;
            //Create a NodeId using the selected ReferenceDescription as browsing starting point
            NodeId nodeId = ExpandedNodeId.ToNodeId(refDesc.NodeId, null);
            //Browse from starting point for all object types
            mSession.Browse(null, null, nodeId, 0u, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences, true, 0, out continuationPoint, out referenceDescriptionCollection);

            while (continuationPoint != null)
            {
                mSession.BrowseNext(null, false, continuationPoint, out revisedContinuationPoint, out nextreferenceDescriptionCollection);
                referenceDescriptionCollection.AddRange(nextreferenceDescriptionCollection);
                continuationPoint = revisedContinuationPoint;
            }

            return referenceDescriptionCollection;
        }

        public NodeId GetParentNode(NodeId nodeId)
        {
            ReferenceDescriptionCollection referenceDescriptionCollection;
            byte[] continuationPoint;
            mSession.Browse(null, null, nodeId, 1, BrowseDirection.Inverse, ReferenceTypeIds.HierarchicalReferences, true, 0, out continuationPoint, out referenceDescriptionCollection);
            return ExpandedNodeId.ToNodeId(referenceDescriptionCollection[0].NodeId, null);
        }


        /// <summary>Browses a node ID provided by a ReferenceDescription</summary>
        /// <param name="refDesc">The ReferenceDescription</param>
        /// <param name="refTypeId">The reference type id</param>
        /// <returns>ReferenceDescriptionCollection of found nodes</returns>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public ReferenceDescriptionCollection BrowseNodeByReferenceType(ReferenceDescription refDesc, BrowseDirection browseDirection, NodeId refTypeId)
        {
            //Create a collection for the browse results
            ReferenceDescriptionCollection referenceDescriptionCollection;
            ReferenceDescriptionCollection nextreferenceDescriptionCollection;
            //Create a continuationPoint
            byte[] continuationPoint;
            byte[] revisedContinuationPoint;
            //Create a NodeId using the selected ReferenceDescription as browsing starting point
            NodeId nodeId = ExpandedNodeId.ToNodeId(refDesc.NodeId, null);
            //Browse from starting point for all object types
            mSession.Browse(null, null, nodeId, 0u, browseDirection, refTypeId, true, 0, out continuationPoint, out referenceDescriptionCollection);

            while (continuationPoint != null)
            {
                mSession.BrowseNext(null, false, continuationPoint, out revisedContinuationPoint, out nextreferenceDescriptionCollection);
                referenceDescriptionCollection.AddRange(nextreferenceDescriptionCollection);
                continuationPoint = revisedContinuationPoint;
            }

            return referenceDescriptionCollection;
        }
        #endregion

        #region Subscription
        /// <summary>Creats a Subscription object to a server</summary>
        /// <param name="publishingInterval">The publishing interval</param>
        /// <returns>Subscription</returns>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public Subscription Subscribe(int publishingInterval, string name)
        {
            //Create a Subscription object
            Subscription subscription = new Subscription(mSession.DefaultSubscription);
            //Enable publishing
            subscription.PublishingEnabled = true;
            //Set the publishing interval
            subscription.PublishingInterval = publishingInterval;
            //Add a subscription name
            subscription.DisplayName = name;
            //Add the subscription to the session
            mSession.AddSubscription(subscription);
            //Create/Activate the subscription
            subscription.Create();
            return subscription;
        }

        /// <summary>Ads a monitored item to an existing subscription</summary>
        /// <param name="subscription">The subscription</param>
        /// <param name="nodeIdString">The node Id as string</param>
        /// <param name="itemName">The name of the item to add</param>
        /// <param name="samplingInterval">The sampling interval</param>
        /// <returns>The added item</returns>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public MonitoredItem AddMonitoredItem(Subscription subscription, string nodeIdString, string itemName, int samplingInterval)
        {
            //Create a monitored item
            MonitoredItem monitoredItem = new MonitoredItem();
            //Set the name of the item for assigning items and values later on; make sure item names differ
            monitoredItem.DisplayName = itemName;
            //Set the NodeId of the item
            monitoredItem.StartNodeId = nodeIdString;
            //Set the attribute Id (value here)
            monitoredItem.AttributeId = Attributes.Value;
            //Set reporting mode
            monitoredItem.MonitoringMode = MonitoringMode.Reporting;
            //Set the sampling interval (1 = fastest possible)
            monitoredItem.SamplingInterval = samplingInterval;
            //Set the queue size
            monitoredItem.QueueSize = 1;
            //Discard the oldest item after new one has been received
            monitoredItem.DiscardOldest = true;
            //Define event handler for this item and then add to monitoredItem
            monitoredItem.Notification += new MonitoredItemNotificationEventHandler(Notification_MonitoredItem);
            //Add the item to the subscription
            subscription.AddItem(monitoredItem);
            //Apply changes to the subscription
            subscription.ApplyChanges();
            return monitoredItem;
        }

        /// <summary>Ads a monitored event item to an existing subscription</summary>
        /// <param name="subscription">The subscription</param>
        /// <param name="nodeIdString">The node Id as string</param>
        /// <param name="itemName">The name of the item to add</param>
        /// <param name="samplingInterval">The sampling interval</param>
        /// <param name="filter">The event filter</param>
        /// <returns>The added item</returns>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public MonitoredItem AddEventMonitoredItem(Subscription subscription, string nodeIdString, string itemName, int samplingInterval, EventFilter filter)
        {
            //Create a monitored item
            MonitoredItem monitoredItem = new MonitoredItem(subscription.DefaultItem);
            //Set the name of the item for assigning items and values later on; make sure item names differ
            monitoredItem.DisplayName = itemName;
            //Set the NodeId of the item
            monitoredItem.StartNodeId = nodeIdString;
            //Set the attribute Id (value here)
            monitoredItem.AttributeId = Attributes.EventNotifier;
            //Set reporting mode
            monitoredItem.MonitoringMode = MonitoringMode.Reporting;
            //Set the sampling interval (1 = fastest possible)
            monitoredItem.SamplingInterval = samplingInterval;
            //Set the queue size
            monitoredItem.QueueSize = 1;
            //Discard the oldest item after new one has been received
            monitoredItem.DiscardOldest = true;
            //Set the filter for the event item
            monitoredItem.Filter = filter;

            //Define event handler for this item and then add to monitoredItem
            Session.Notification += new NotificationEventHandler(Notification_MonitoredEventItem);

            //Add the item to the subscription
            subscription.AddItem(monitoredItem);
            //Apply changes to the subscription
            subscription.ApplyChanges();
            return monitoredItem;
        }

        /// <summary>Removs a monitored item from an existing subscription</summary>
        /// <param name="subscription">The subscription</param>
        /// <param name="monitoredItem">The item</param>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public MonitoredItem RemoveMonitoredItem(Subscription subscription, MonitoredItem monitoredItem)
        {
            //Add the item to the subscription
            subscription.RemoveItem(monitoredItem);
            //Apply changes to the subscription
            subscription.ApplyChanges();
            return null;
        }

        /// <summary>Removes an existing Subscription</summary>
        /// <param name="subscription">The subscription</param>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public void RemoveSubscription(Subscription subscription)
        {
            //Delete the subscription and all items submitted
            subscription.Delete(true);
        }
        #endregion

        #region Read/Write
        /// <summary>Reads a node by node Id</summary>
        /// <param name="nodeIdString">The node Id as string</param>
        /// <returns>The read node</returns>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public Node ReadNode(String nodeIdString)
        {
            //Create a nodeId using the identifier string
            NodeId nodeId = new NodeId(nodeIdString);
            //Create a node
            Node node = new Node();
            //Read the dataValue
            node = mSession.ReadNode(nodeId);
            return node;
        }

        /// <summary>Reads a node by node Id</summary>
        /// <param name="nodeIdString">The node Id as string</param>
        /// <returns>A dictionary containing the attribute values of the node using the attribute uint identifier as key; 
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public Dictionary<uint, DataValue> ReadNodeAttributes(String nodeIdString)
        {
            Dictionary<int, uint> attributeDictionary = new Dictionary<int, uint>()
            {
                {1, Attributes.NodeId },
                {2, Attributes.NodeClass },
                {3, Attributes.BrowseName },
                {4, Attributes.DisplayName },
                {5, Attributes.Value },
                {6, Attributes.DataType},
                {7, Attributes.ValueRank},
                {8, Attributes.ArrayDimensions},
                {9, Attributes.AccessLevel }
            };
            //Create a nodeId using the identifier string
            NodeId nodeId = new NodeId(nodeIdString);

            //Create a read value id collection to store the nodes to read
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            //Create a StatusCodeCollection
            DataValueCollection dataValueCollection = new DataValueCollection();

            //Create a DiagnosticInfoCollection
            DiagnosticInfoCollection diag = new DiagnosticInfoCollection();

            //Go through the attribute dictionary and create the nodes accordingly
            for (int i = 0; i < attributeDictionary.Count; i++)
            {
                ///Create a read value id with the nessessary attribute
                ReadValueId nodeToRead = new ReadValueId();
                nodeToRead.NodeId = nodeId;
                nodeToRead.AttributeId = attributeDictionary[i+1];
                nodesToRead.Add(nodeToRead);
            }
            //Read the read value id collection
            mSession.Read(null, 0, TimestampsToReturn.Neither, nodesToRead, out dataValueCollection, out diag);
            //Create a dictionary to store the attribute values
            Dictionary<uint, DataValue> attributeValues = new Dictionary<uint, DataValue>();
            //Go through the data value collection to store every data value according to its attribute identifier
            int counter = 1;
            foreach (DataValue dataValue in dataValueCollection)
            {
                attributeValues.Add(attributeDictionary[counter], dataValue);
                counter += 1;
            }
            return attributeValues;
        }

        /// <summary>Reads values from node Ids</summary>
        /// <param name="nodeIdStrings">The node Ids as strings</param>
        /// <returns>The read values as strings</returns>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public List<string> ReadValues(List<string> nodeIdStrings)
        {
            List<NodeId> nodeIds = new List<NodeId>();
            List<Type> types = new List<Type>();
            List<object> values;
            List<ServiceResult> serviceResults;
            foreach (string str in nodeIdStrings)
            {
                //Create a nodeId using the identifier string and add to list
                nodeIds.Add(new NodeId(str));
                //No need for types
                types.Add(null);
            }
            //Read the dataValues
            mSession.ReadValues(nodeIds, types, out values, out serviceResults);
            //Check ServiceResults to 
            foreach (ServiceResult svResult in serviceResults)
            {
                if (svResult.ToString() != "Good")
                {
                    Exception e = new Exception(svResult.ToString());
                    throw e;
                }
            }
            //Create result string
            List<string> resultStrings = new List<string>();
            foreach (object result in values)
            {
                if (result != null)
                {
                    //Check if result is an array or base data type
                    if (result is Array)
                    {
                        List<string> elements = new List<string>();
                        foreach (var elementResult in result as Array)
                        {
                            elements.Add(elementResult is byte ? ((byte)elementResult).ToString("X2") : elementResult.ToString());
                        }
                        resultStrings.Add(String.Join("\0", elements));
                    }
                    else if (result is Matrix) //Check if result is a matrix
                    {
                        List<string> matrixStrings = new List<string>();
                        Matrix matrix = result as Matrix;
                        int matrixLength = matrix.Elements.Length;
                        for (int i = 0; i < matrixLength; i++)
                        {
                            matrixStrings.Add(matrix.Elements.GetValue(i).ToString());
                        }
                        resultStrings.Add(String.Join("\0", matrixStrings));
                    }
                    else //The result is a scalar
                    {
                        resultStrings.Add(result.ToString());
                    }
                }
                else
                {
                    resultStrings.Add("(null)");
                }
            }
            return resultStrings;
        }

        /// <summary>Writes values to node Ids</summary>
        /// <param name="valuesByNodeId">Mulitple NodeIds with there values (multiple possible if not a scalar).</param>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public void WriteValues(Dictionary<NodeId, IEnumerable<string>> valuesByNodeId)
        {
            //Create a collection of values to write
            WriteValueCollection valuesToWrite = new WriteValueCollection();

            foreach (var keyValuePair in valuesByNodeId)
            {
                NodeId nodeId = keyValuePair.Key;
                IEnumerable<string> values = keyValuePair.Value;

                //Get the OPC UA data type and the Rank
                Node node = mSession.ReadNode(nodeId);
                VariableNode variableNode = (VariableNode)node.DataLock;
                NodeId datatypeId = variableNode.DataType;
                BuiltInType targetype = TypeInfo.GetBuiltInType(datatypeId);

                //Ensure that target type is not null by reading the node of the data type id
                if (targetype == BuiltInType.Null)
                {
                    try
                    {
                        //Get the node id of the parent base data type
                        NodeId tempNodeId = GetParentDataType(datatypeId, mSession);
                        targetype = TypeInfo.GetBuiltInType(tempNodeId);
                        if (targetype == BuiltInType.Null)//Ensure that the entered node id is not of type struct
                        {
                            throw new Exception("The entered node id may be of type struct. Please use the methods for read/write structs.");
                        }
                    }
                    catch
                    {
                        throw new Exception("The node id data type is not castable. Please check the data type.");
                    }

                }
                int valueRank = variableNode.ValueRank;
                DataValue dataValue = null;

                //Check if there is a value entered
                if (!values.Any() || nodeId == null) //no value in the textbox
                {
                    throw new Exception("There is no NodeId or value entered.");
                }

                //Ensure that boolean entries have lower case initial and float, double and long double have the rigth format
                List<string> tempValues = new List<string>();
                if (targetype == BuiltInType.Boolean)
                {
                    foreach (string tempValue in values)
                    {
                        tempValues.Add(tempValue.ToLower());
                    }
                    values = tempValues;
                }
                if (targetype == BuiltInType.Float || targetype == BuiltInType.Double)
                {
                    foreach (string tempValue in values)
                    {
                        tempValues.Add(tempValue.Replace(',', '.'));
                    }
                    values = tempValues;
                }

                //Check if the inputed value has the type matrix or array
                if (valueRank >= ValueRanks.OneDimension)
                {
                    //Cast values in the target type
                    Array castValues = TypeInfo.CastArray(values.ToArray(), BuiltInType.Null, targetype, (object source, BuiltInType srcType, BuiltInType dstType) => TypeInfo.Cast(source, dstType));

                    if (valueRank == ValueRanks.OneDimension) //Check if inputed value is array
                    {
                        dataValue = new DataValue(new Variant(castValues, new TypeInfo(targetype, valueRank)));
                    }
                    else //Inputed value has more then one dimension
                    {
                        //Get matrix dimensions
                        int[] dimensions = new int[valueRank];
                        for (int i = 0; i < valueRank; i++)
                        {
                            dimensions[i] = (int)variableNode.ArrayDimensions[i];
                        }
                        Matrix matrix = new Matrix(castValues, targetype, dimensions);
                        dataValue = new DataValue(new Variant(matrix));
                    }
                }
                else
                {
                    try //OPC UA data type with type scalar
                    {
                        dataValue = new DataValue(new Variant(TypeInfo.Cast(values.First(), targetype)));
                    }
                    catch //no OPC UA data type
                    {
                        throw new FormatException($"The value inputed [{values.First()}] is not convertable to type {targetype}.");
                    }
                }
                //Create a WriteValue using the NodeId, dataValue and attributeType
                WriteValue valueToWrite = new WriteValue()
                {
                    Value = dataValue,
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
                };

                //Add the dataValues to the collection
                valuesToWrite.Add(valueToWrite);
            }

            StatusCodeCollection result;
            //Write the collection to the server
            mSession.Write(null, valuesToWrite, out result, out _);
            foreach (StatusCode code in result)
            {
                if (code != 0)
                {
                    throw new Exception(code.ToString());
                }
            }
            MessageBox.Show("Values written successfully.", "Success");
        }
        #endregion

        #region Read/Write Struct/UDT
        /// <summary>Reads a struct or UDT by node Id</summary>
        /// <param name="nodeIdString">The node Id as strings</param>
        /// <returns>The read struct/UDT elements as a list of string[3]; string[0] = tag name, string[1] = value, string[2] = opc data type</returns>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public List<string[]> ReadStructUdt(String nodeIdString)
        {
            //Define result list to return var name and var value
            List<string[]> resultStringList = new List<string[]>();

            //Read the node attributes
            Dictionary<uint, DataValue> nodeAttributes = ReadNodeAttributes(nodeIdString);
            //Create a data value storing the value
            DataValue data = nodeAttributes[Attributes.Value];

            //Check if the entered node id is of type array and accordingly get the byte array of the data values
            ExtensionObject dataValue = new ExtensionObject();
            List<byte[]> byteArrays = new List<byte[]>();
            if (data.Value is ExtensionObject[])
            {
                foreach (ExtensionObject temp in (ExtensionObject[])data.Value)
                {
                    byteArrays.Add((byte[])temp.Body);
                }
            }
            else if (data.Value is ExtensionObject)
            {
                dataValue = (ExtensionObject)data.Value;
                byteArrays.Add((byte[])dataValue.Body);
            }
            else
            {
                throw new Exception("The entered node id is not of tpye struct/udt.");
            }

            //Get the type dictionary of the struct as a list of string[3]; string[0] = tag name, string[1] = data type, string [2] = array dimensions
            List<string[]> structureDictionary = new List<string[]>();
            DataTypeNode dataTypeNode = (DataTypeNode)ReadNode(nodeAttributes[Attributes.DataType].ToString());
            GetTypeDictionary(dataTypeNode, mSession, structureDictionary);

            //Get the deserialized byte string as a list of string[4]; string[0]=array index; string[1]=tag name; string[2]=tag value; string[3]=tag data type
            resultStringList = ParseDataToTagsFromDictionary(structureDictionary, byteArrays);

            //return result as List<string[]> string[0]=array index; string[1]=tag name; string[2]=tag value; string[3]=tag data type
            return resultStringList;
        }

        /// <summary>Writes data to a struct or UDT by node Id</summary>
        /// <param name="nodeIdString">The node Id as strings</param>
        /// <param name="dataToWrite">The data to write as string[3]; string[0] = tag name, string[1] = value, string[2] = opc data type</param>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public void WriteStructUdt(String nodeIdString, List<string[]> dataToWrite)
        {
            //Create a NodeId from the NodeIdString
            NodeId nodeId = new NodeId(nodeIdString);

            //Creat a WriteValueColelction
            WriteValueCollection valuesToWrite = new WriteValueCollection();

            //Create a WriteValue
            WriteValue writevalue = new WriteValue();

            //Read the node attributes
            Dictionary<uint, DataValue> nodeAttributes = ReadNodeAttributes(nodeIdString);
            //Create a data value storing the value
            DataValue data = nodeAttributes[Attributes.Value];

            //Check if the entered node id is of type array and accordingly get the byte array(s) of the data values
            ExtensionObject extensionObject = new ExtensionObject();
            List<byte[]> byteArrays = new List<byte[]>();
            if (data.Value is ExtensionObject[])
            {
                foreach (ExtensionObject temp in (ExtensionObject[])data.Value)
                {
                    byteArrays.Add((byte[])temp.Body);
                }
            }
            else
            {
                extensionObject = (ExtensionObject)data.Value;
                byteArrays.Add((byte[])extensionObject.Body);
            }

            //Get the type dictionary of the struct as a list of string[3]; string[0] = tag name, string[1] = data type, string [2] = array dimensions
            List<string[]> structureDictionary = new List<string[]>();
            DataTypeNode dataTypeNode = (DataTypeNode)ReadNode(nodeAttributes[Attributes.DataType].ToString());
            GetTypeDictionary(dataTypeNode, mSession, structureDictionary);

            //Delete empty list elements in the structure dictionary and in the list of the gui
            for (int j = 0; j<structureDictionary.Count; j++)
            {
                if (structureDictionary[j][1] == "")//Check if the data type entry is empty as this is an indicator for a struct/udt in a struct/udt
                {
                    structureDictionary.RemoveAt(j);
                    //dataToWrite.RemoveAt(j);
                }
            }

            //Create a byte array
            List<byte[]> bytesToWrite;

            //Parse dataToWrite to the byte array
            bytesToWrite = ParseDataToByteArray(dataToWrite, structureDictionary);

            //Create a StatusCodeCollection
            StatusCodeCollection results = new StatusCodeCollection();

            //Create a DiagnosticInfoCollection
            DiagnosticInfoCollection diag = new DiagnosticInfoCollection();

            //Get the structure definition
            StructureDefinition structureDefinition = (StructureDefinition)dataTypeNode.DataTypeDefinition.Body;

            //Create an ExtensionObject from the Structure given to this function
            ExtensionObject writeExtObj = new ExtensionObject();

            //Copy the encoding node id to the extension object type id
            writeExtObj.TypeId = structureDefinition.DefaultEncodingId;

            //Copy data to extension object body
            DataValue dataValue = null;

            if (bytesToWrite.Count == 1)
            {
                //Copy data to extension object body
                writeExtObj.Body = bytesToWrite[0];
                //Turn the created ExtensionObject into a DataValue
                dataValue = new DataValue(writeExtObj);
            }
            else
            {
                ExtensionObject[] writeExteObjArr = new ExtensionObject[bytesToWrite.Count];
                for (int i = 0; i < bytesToWrite.Count; i++)
                {
                    //Create an ExtensionObject from the Structure given to this function
                    writeExtObj = new ExtensionObject();
                    //Copy data to extension object body
                    writeExtObj.TypeId = structureDefinition.DefaultEncodingId;
                    writeExtObj.Body = bytesToWrite[i];
                    //Copy extension object to extension object array
                    writeExteObjArr[i] = writeExtObj;
                }
                //Turn the created ExtensionObject[] into a DataValue
                dataValue = new DataValue(writeExteObjArr);
            }
            //Setup for the WriteValue
            writevalue.NodeId = nodeId;
            writevalue.Value = dataValue;
            writevalue.AttributeId = Attributes.Value;
            //Add the created value to the collection
            valuesToWrite.Add(writevalue);

            try
            {
                mSession.Write(null, valuesToWrite, out results, out diag);
            }
            catch (Exception e)
            {
                //Handle Exception here
                throw e;
            }

            //Check result codes
            foreach (StatusCode result in results)
            {
                if (result.ToString() != "Good")
                {
                    Exception e = new Exception(result.ToString());
                    throw e;
                }
            }
            MessageBox.Show("Values written successfully.", "Success");
        }
        #endregion

        #region Register/Unregister nodes Ids
        /// <summary>Registers Node Ids to the server</summary>
        /// <param name="nodeIdStrings">The node Ids as strings</param>
        /// <returns>The registered Node Ids as strings</returns>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public List<string> RegisterNodeIds(List<String> nodeIdStrings)
        {
            NodeIdCollection nodesToRegister = new NodeIdCollection();
            NodeIdCollection registeredNodes = new NodeIdCollection();
            List<string> registeredNodeIdStrings = new List<string>();
            foreach (string str in nodeIdStrings)
            {
                //Create a nodeId using the identifier string and add to list
                nodesToRegister.Add(new NodeId(str));
            }
            //Register nodes
            mSession.RegisterNodes(null, nodesToRegister, out registeredNodes);

            foreach (NodeId nodeId in registeredNodes)
            {
                registeredNodeIdStrings.Add(nodeId.ToString());
            }

            return registeredNodeIdStrings;
        }

        /// <summary>Unregister Node Ids to the server</summary>
        /// <param name="nodeIdStrings">The node Ids as string</param>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public void UnregisterNodeIds(List<String> nodeIdStrings)
        {
            NodeIdCollection nodesToUnregister = new NodeIdCollection();
            List<string> registeredNodeIdStrings = new List<string>();
            foreach (string str in nodeIdStrings)
            {
                //Create a nodeId using the identifier string and add to list
                nodesToUnregister.Add(new NodeId(str));
            }
            //Register nodes                
            mSession.UnregisterNodes(null, nodesToUnregister);
        }
        #endregion

        #region Methods
        /// <summary>Get information about a method's input and output arguments</summary>
        /// <param name="nodeIdString">The node Id of a method as strings</param>
        /// <returns>Argument informations as strings</returns>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public List<string> GetMethodArguments(String nodeIdString)
        {
            //Return input argument node informations
            //Argument[0] = argument type (input or output); 
            //Argument[1] = argument name
            //Argument[2] = argument value
            //Argument[3] = argument data type
            List<string> arguments = new List<string>();

            //Create node id object by node id string
            NodeId nodeId = new NodeId(nodeIdString);

            //Check if node is method
            Dictionary<uint, DataValue> methodNode = ReadNodeAttributes(nodeIdString);
            dynamic methodNodeClassAttribute = methodNode[Attributes.NodeClass].Value;

            if (methodNodeClassAttribute == ((int)NodeClass.Method))
            {
                //We need to browse for property (input and output arguments)
                //Create a collection for the browse results
                ReferenceDescriptionCollection referenceDescriptionCollection;
                ReferenceDescriptionCollection nextreferenceDescriptionCollection;
                //Create a continuationPoint
                byte[] continuationPoint;
                byte[] revisedContinuationPoint;

                //Start browsing

                //Browse from starting point for properties (input and output)
                mSession.Browse(null, null, nodeId, 0u, BrowseDirection.Forward, ReferenceTypeIds.HasProperty, true, 0, out continuationPoint, out referenceDescriptionCollection);

                while (continuationPoint != null)
                {
                    mSession.BrowseNext(null, false, continuationPoint, out revisedContinuationPoint, out nextreferenceDescriptionCollection);
                    referenceDescriptionCollection.AddRange(nextreferenceDescriptionCollection);
                    continuationPoint = revisedContinuationPoint;
                }

                //Check if arguments exist
                if (referenceDescriptionCollection != null & referenceDescriptionCollection.Count > 0)
                {
                    foreach (ReferenceDescription refDesc in referenceDescriptionCollection)
                    {
                        if (refDesc.DisplayName.Text == "InputArguments" || refDesc.DisplayName.Text == "OutputArguments" && refDesc.NodeClass == NodeClass.Variable)
                        {
                            List<NodeId> nodeIds = new List<NodeId>();
                            List<Type> types = new List<Type>();
                            List<object> values = new List<object>();
                            List<ServiceResult> serviceResults = new List<ServiceResult>();

                            nodeIds.Add(new NodeId(refDesc.NodeId.ToString()));
                            types.Add(null);

                            //Read the input/output arguments
                            mSession.ReadValues(nodeIds, types, out values, out serviceResults);

                            foreach (ServiceResult svResult in serviceResults)
                            {
                                if (svResult.ToString() != "Good")
                                {
                                    Exception e = new Exception(svResult.ToString());
                                    throw e;
                                }
                            }

                            //Extract arguments
                            foreach (object result in values)
                            {
                                if (result != null)
                                {
                                    //Cast object to ExtensionObject because input and output arguments are always extension objects
                                    ExtensionObject encodeable = result as ExtensionObject;
                                    if (encodeable == null)
                                    {
                                        ExtensionObject[] exObjArr = result as ExtensionObject[];
                                        foreach (ExtensionObject exOb in exObjArr)
                                        {
                                            Argument arg = exOb.Body as Argument;
                                            string[] argumentInfos = new string[4];
                                            // Set type: input or output
                                            argumentInfos[0] = refDesc.DisplayName.Text;
                                            // Set argument name
                                            argumentInfos[1] = arg.Name;
                                            // Set argument value
                                            if (arg.Value != null)
                                            {
                                                argumentInfos[2] = arg.Value.ToString();
                                                //You might have to cast the value appropriate
                                                //TBD
                                            }
                                            else
                                            {
                                                argumentInfos[2] = "";
                                            }

                                            //Set argument data type (no array)
                                            if (arg.ArrayDimensions.Count == 0)
                                            {
                                                Node node = ReadNode(arg.DataType.ToString());
                                                argumentInfos[3] = node.DisplayName.ToString();
                                            }
                                            // Data type is array
                                            else if (arg.ArrayDimensions.Count == ValueRanks.OneDimension)
                                            {
                                                Node node = ReadNode(arg.DataType.ToString());
                                                argumentInfos[3] = node.DisplayName.ToString() + "[" + arg.ArrayDimensions[0].ToString() + "]";
                                            }

                                            arguments.Add(String.Join(";", argumentInfos));
                                        }
                                    }
                                    else
                                    {
                                        arguments.Add(encodeable.ToString());
                                    }

                                }
                            }
                        }
                    }
                }
                else
                {
                    return arguments;
                }

                return arguments;
            }
            else
            {
                //Not method; return null
                return null;
            }

        }

        /// <summary>Calls a method</summary>
        /// <param name="methodIdString">The node Id as strings</param>
        /// <param name="objectIdString">The object Id as strings</param>
        /// <param name="inputData">The input argument data</param>
        /// <returns>The list of output arguments</returns>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        public IList<object> CallMethod(NodeId methodId, NodeId objectId, List<string[]> inputData)
        {
            //Declare an array of objects for the method's input arguments
            Object[] inputArguments = new object[inputData.Count];

            GetMethodInformation(methodId, out List<NodeId> dataTypeIds, out List<int> valueRanks, out List<uint> arrayLengths);
            //Parse data types first
            for (int i = 0; i < inputData.Count; i++)
            {
                string inputValue = inputData[i][0];
                BuiltInType targetype = TypeInfo.GetBuiltInType(dataTypeIds[i]);

                //TBD i.e. methods with input arguments of type vector
                if (targetype == 0)
                {
                    //Node test = mSession.ReadNode(dataTypeIds[i]);
                    Exception e = new Exception("Due to its input arguments, the method is currently not supported.");
                    throw e;
                }

                if (valueRanks[i] == ValueRanks.OneDimension)
                {
                    string[] tempArr = inputData[i][0].Split(';');
                    inputArguments[i] = TypeInfo.CastArray(tempArr, BuiltInType.Null, targetype, (object source, BuiltInType srcType, BuiltInType dstType) => TypeInfo.Cast(source, dstType)); ;
                }
                else if (inputData[i][1] == "ByteString")
                {
                    int NumberChars = inputData[i][0].Length;
                    if (NumberChars % 2 == 1)
                    {
                        Exception e = new Exception("Check length of ByteString");
                        throw e;
                    }
                    Byte[] value = new Byte[NumberChars / 2];
                    for (int j = 0; j < NumberChars; j += 2)
                    {
                        value[j / 2] = Convert.ToByte(inputData[i][0].Substring(j, 2), 16);
                    }
                    inputArguments[i] = value;
                }
                else if (valueRanks[i] == ValueRanks.Scalar) //scalar data type
                {
                    inputArguments[i] = TypeInfo.Cast(inputValue, targetype);
                }
                else //no base data type // needs to be tested
                {
                    throw new FormatException($"The value inputed [{inputValue}] is not convertable to type {targetype}.");
                }
            }

            //Declare a list of objects for the method's output arguments
            IList<object> outputArguments = new List<object>();

            //Call the method
            outputArguments = mSession.Call(objectId, methodId, inputArguments);

            return outputArguments;
        }
        #endregion

        #region EventHandling
        /// <summary>Eventhandler to validate the server certificate forwards this event</summary>
        private void Notification_CertificateValidation(CertificateValidator certificate, CertificateValidationEventArgs e)
        {
            CertificateValidationNotification(certificate, e);
        }

        /// <summary>Eventhandler for MonitoredItemNotifications forwards this event</summary>
        private void Notification_MonitoredItem(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            ItemChangedNotification(monitoredItem, e);
        }

        /// <summary>Eventhandler for MonitoredItemNotifications for event items forwards this event</summary>
        private void Notification_MonitoredEventItem(ISession session, NotificationEventArgs e)
        {
            NotificationMessage message = e.NotificationMessage;

            // Check for keep alive.
            if (message.NotificationData.Count == 0)
            {
                return;
            }

            ItemEventNotification(session, e);
        }

        /// <summary>Eventhandler for KeepAlive forwards this event</summary>
        private void Notification_KeepAlive(ISession session, KeepAliveEventArgs e)
        {
            KeepAliveNotification(session, e);
        }


        #endregion

        #region Private methods
        private static void CreateCertificateAndAddToStore(string applicationUri, string applicationName, string storeType, string storePath)
        {
            List<string> localIps = GetLocalIpAddressAndDns(); // Get local interface ip addresses and DNS name
            ushort keySize = 2048; //must be multiples of 1024
            ushort lifeTimeInMonths = 24; //month till certificate expires
            ushort hashSizeInBits = 256; //0 = SHA1; 1 = SHA256
            var startTime = System.DateTime.Now; //starting point of time when certificate is valid

            var certificateBuilder = CertificateFactory.CreateCertificate(
                applicationUri,
                applicationName,
                null,
                localIps);

            X509Certificate2 clientCertificate2 = certificateBuilder
                .SetNotBefore(startTime)
                .SetNotAfter(startTime.AddMonths(lifeTimeInMonths))
                .SetHashAlgorithm(X509Utils.GetRSAHashAlgorithmName(hashSizeInBits))
                .SetRSAKeySize(keySize)
                .CreateForRSA()
                .AddToStore(
                    storeType,
                    storePath,
                    null
                );
        }
        /// <summary>
        /// Handles a reconnect event complete from the reconnect handler
        /// </summary>
        private void GetMethodInformation(NodeId methodId, out List<NodeId> dataTypeIds, out List<int> valueRanks, out List<uint> arrayLengths)
        {
            //We need to browse for property (input and output arguments)
            //Create a collection for the browse results
            ReferenceDescriptionCollection referenceDescriptionCollection;
            //Create a continuationPoint
            byte[] continuationPoint;

            mSession.Browse(
                        null,
                        null,
                        methodId,
                        0u,
                        BrowseDirection.Forward,
                        ReferenceTypeIds.HasProperty,
                        true,
                        0,
                        out continuationPoint,
                        out referenceDescriptionCollection);

            List<NodeId> nodeIdInputArguments = new List<NodeId>();
            List<Type> types = new List<Type>();

            foreach (ReferenceDescription refDesc in referenceDescriptionCollection)
            {
                if (refDesc.BrowseName == "InputArguments")
                {
                    nodeIdInputArguments.Add(new NodeId(refDesc.NodeId.ToString()));
                    types.Add(null);
                }
            }
            List<object> inputValues = new List<object>();
            List<ServiceResult> serviceResults = new List<ServiceResult>();
            dataTypeIds = new List<NodeId>();
            valueRanks = new List<int>();
            arrayLengths = new List<uint>();
            //Read the input/output arguments
            mSession.ReadValues(nodeIdInputArguments, types, out inputValues, out serviceResults);


            foreach (object result in inputValues)
            {
                //Cast object to ExtensionObject because input and output arguments are always extension objects
                ExtensionObject encodeable = result as ExtensionObject;
                if (encodeable == null)
                {
                    ExtensionObject[] exObjArr = result as ExtensionObject[];
                    foreach (ExtensionObject exOb in exObjArr)
                    {
                        //Get the data types of the input arguments
                        Argument arg = exOb.Body as Argument;
                        dataTypeIds.Add(arg.DataType);
                        //Check if input argument is scalar
                        if (arg.ArrayDimensions.Count == 0)
                        {
                            valueRanks.Add(ValueRanks.Scalar);
                        }
                        //Input argument is array or matrix
                        else
                        {
                            valueRanks.Add(arg.ArrayDimensions.Count);
                            uint arrayLength = 0;
                            for (int j = 0; j < arg.ArrayDimensions.Count; j++)
                            {
                                arrayLength *=arg.ArrayDimensions.ElementAtOrDefault(j);
                            }
                            arrayLengths.Add(arrayLength);
                        }
                    }
                }
            }
        }

        /// <summary>Creats a minimal required ApplicationConfiguration</summary>
        /// <param name="localIpAddress">The ip address of the interface to connect with</param>
        /// <returns>The ApplicationConfiguration</returns>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        private static ApplicationConfiguration CreateClientConfiguration()
        {
            // The application configuration can be loaded from any file.
            // ApplicationConfiguration.Load() method loads configuration by looking up a file path in the App.config.
            // This approach allows applications to share configuration files and to update them.
            // This example creates a minimum ApplicationConfiguration using its default constructor.
            ApplicationConfiguration configuration = new ApplicationConfiguration();

            // Step 1 - Specify the client identity.
            configuration.ApplicationName = "UA Client 1500";
            configuration.ApplicationType = ApplicationType.Client;
            configuration.ApplicationUri = "urn:MyClient"; //Kepp this syntax
            configuration.ProductUri = "SiemensAG.IndustryOnlineSupport";

            // Step 2 - Specify the client's application instance certificate.
            // Application instance certificates must be placed in a windows certficate store because that is 
            // the best way to protect the private key. Certificates in a store are identified with 4 parameters:
            // StoreLocation, StoreName, SubjectName and Thumbprint.
            // When using StoreType = Directory you need to have the opc.ua.certificategenerator.exe installed on your machine

            configuration.SecurityConfiguration = new SecurityConfiguration();
            configuration.SecurityConfiguration.ApplicationCertificate = new CertificateIdentifier();
            configuration.SecurityConfiguration.ApplicationCertificate.StoreType = CertificateStoreType.X509Store;
            configuration.SecurityConfiguration.ApplicationCertificate.StorePath = "CurrentUser\\My";
            configuration.SecurityConfiguration.ApplicationCertificate.SubjectName = configuration.ApplicationName;
            configuration.SecurityConfiguration.AutoAcceptUntrustedCertificates = true;
            configuration.SecurityConfiguration.RejectSHA1SignedCertificates = false;

            // Define trusted root store for server certificate checks
            configuration.SecurityConfiguration.TrustedIssuerCertificates.StoreType = CertificateStoreType.X509Store;
            configuration.SecurityConfiguration.TrustedIssuerCertificates.StorePath = "CurrentUser\\Root";
            configuration.SecurityConfiguration.TrustedPeerCertificates.StoreType = CertificateStoreType.X509Store;
            configuration.SecurityConfiguration.TrustedPeerCertificates.StorePath = "CurrentUser\\Root";

            // find the client certificate in the store.
            Task<X509Certificate2> clientCertificate = configuration.SecurityConfiguration.ApplicationCertificate.Find(true);

            // create a new self signed certificate if not found.
            if (clientCertificate.Result == null)
            {
                CreateCertificateAndAddToStore(configuration.ApplicationUri, configuration.ApplicationName, configuration.SecurityConfiguration.ApplicationCertificate.StoreType, configuration.SecurityConfiguration.ApplicationCertificate.StorePath);
            }

            // Step 3 - Specify the supported transport quotas.
            // The transport quotas are used to set limits on the contents of messages and are
            // used to protect against DOS attacks and rogue clients. They should be set to
            // reasonable values.
            configuration.TransportQuotas = new TransportQuotas();
            configuration.TransportQuotas.OperationTimeout = 360000;
            configuration.TransportQuotas.SecurityTokenLifetime = 86400000;
            configuration.TransportQuotas.MaxStringLength = 67108864;
            configuration.TransportQuotas.MaxByteStringLength = 16777216; //Needed, i.e. for large TypeDictionarys

            // Step 4 - Specify the client specific configuration.
            configuration.ClientConfiguration = new ClientConfiguration();
            configuration.ClientConfiguration.DefaultSessionTimeout = 360000;

            // Step 5 - Validate the configuration.
            // This step checks if the configuration is consistent and assigns a few internal variables
            // that are used by the SDK. This is called automatically if the configuration is loaded from
            // a file using the ApplicationConfiguration.Load() method.
            _ = configuration.Validate(ApplicationType.Client);

            return configuration;
        }

        /// <summary>Creats an EndpointDescription</summary>
        /// <param name="url">The endpoint url</param>
        /// <param name="security">Use security or not</param>
        /// <returns>The EndpointDescription</returns>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        private static EndpointDescription CreateEndpointDescription(string url, string secPolicy, MessageSecurityMode msgSecMode)
        {
            // create the endpoint description.
            EndpointDescription endpointDescription = new EndpointDescription();

            // submit the url of the endopoint
            endpointDescription.EndpointUrl = url;

            // specify the security policy to use.

            endpointDescription.SecurityPolicyUri = secPolicy;
            endpointDescription.SecurityMode = msgSecMode;

            // specify the transport profile.
            endpointDescription.TransportProfileUri = Profiles.UaTcpTransport;

            return endpointDescription;
        }

        /// <summary>Gets the local IP addresses and the DNS name</summary>
        /// <returns>The list of IPs and names</returns>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        private static List<string> GetLocalIpAddressAndDns()
        {
            List<string> localIps = new List<string>();
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    localIps.Add(ip.ToString());
                }
            }
            if (localIps.Count == 0)
            {
                throw new Exception("Local IP Address Not Found!");
            }
            localIps.Add(Dns.GetHostName());
            return localIps;
        }

        /// <summary>Parses a byte array to objects containing tag names and tag data types</summary>
        /// <param name="varList">List of object containing tag names and tag data types</param>
        /// <param name="byteResult">A byte array to parse</param>
        /// <returns>A list of string[4]; string[0] = tag name, string[1] = tag name, string[2] = value, string[3] = opc data type</returns>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        private static List<string[]> ParseDataToTagsFromDictionary(List<string[]> varList, List<byte[]> byteArrays)
        {
            //Define result list to return var name, var value and var data type
            List<string[]> resultStringList = new List<string[]>();

            //Dictionary for index counting
            Dictionary<BuiltInType, int> byteLength = new Dictionary<BuiltInType, int>()
                {
                    {BuiltInType.Boolean, 1},
                    {BuiltInType.Int16, 2},
                    {BuiltInType.Int32, 4},
                    {BuiltInType.Int64, 8},
                    {BuiltInType.UInt16, 2},
                    {BuiltInType.UInt32, 4},
                    {BuiltInType.UInt64, 8},
                    {BuiltInType.Float, 4},
                    {BuiltInType.Double, 8},
                    {BuiltInType.Byte, 1},
                    {BuiltInType.String, 4}
                };

            //Array index
            int arrayIndex = 0;
            foreach (byte[] byteResult in byteArrays)
            {
                //Byte decoding index
                int index = 0;

                //Start decoding for opc data types
                foreach (string[] val in varList)
                {
                    string[] dataReferenceStringArray = new string[4];

                    //Check if the type dictionary includes a struct/udt in a struct/udt and enter an empty row
                    if (val[1] == "")
                    {
                        dataReferenceStringArray[0] = "[" + arrayIndex.ToString() + "]";
                        dataReferenceStringArray[1] = val[0];
                        dataReferenceStringArray[2] = "";
                        dataReferenceStringArray[3] = "";
                        //Add the data Reference string array to the result string list
                        resultStringList.Add(dataReferenceStringArray);
                        continue;
                    }
                    //Add array index
                    dataReferenceStringArray[0] = "[" + arrayIndex.ToString() + "]";
                    //Copy tag name
                    dataReferenceStringArray[1] = val[0];
                    //Get the target data type via the data type id
                    NodeId datatypeId = new NodeId(val[1]);
                    BuiltInType targetType = TypeInfo.GetBuiltInType(datatypeId);
                    //Get the system type
                    Type systemType = TypeInfo.GetSystemType(datatypeId, null);
                    //Parse array dimensions string in int32
                    Int32 arrayDimensions = 0;
                    if (val[2] != "")
                    {
                        arrayDimensions = Int32.Parse(val[2]);
                    }

                    //Deserialize the byte array depending on the target data type and the array dimension
                    if (targetType == BuiltInType.String && !(arrayDimensions  > 0))
                    {
                        //Get the string length
                        Int32 stringlength = BitConverter.ToInt32(byteResult, index);
                        index += byteLength[targetType];
                        if (stringlength > 0) //Decode the bytes to its string value
                        {
                            dataReferenceStringArray[2] = Encoding.UTF8.GetString(byteResult, index, stringlength);
                            index += stringlength;
                        }
                        else
                        {
                            dataReferenceStringArray[2] = "";
                        }
                    }
                    else if (targetType == BuiltInType.String && arrayDimensions  > 0)
                    {
                        //Skip array information (UInt32) regarding its size
                        index += byteLength[BuiltInType.UInt32];
                        //Check every element of the array
                        for (int i = 0; i < arrayDimensions; i++)
                        {
                            //Get the string length
                            Int32 stringlength = BitConverter.ToInt32(byteResult, index);
                            index += byteLength[targetType];
                            if (stringlength > 0) //Decode the bytes to its string value
                            {
                                dataReferenceStringArray[2] = String.Concat(dataReferenceStringArray[2], Encoding.UTF8.GetString(byteResult, index, stringlength));
                                //Add a ; to the string value to seperate the value from the other array values
                                dataReferenceStringArray[2] = String.Concat(dataReferenceStringArray[2], ";");
                                index += stringlength;
                            }
                            else
                            {
                                dataReferenceStringArray[2] = dataReferenceStringArray[2] + ";";
                            }
                        }
                    }
                    else if (targetType == BuiltInType.Byte && !(arrayDimensions > 0))
                    {
                        //Copy the byte value as string to the data reference array
                        dataReferenceStringArray[2] = byteResult[index].ToString();
                        index += byteLength[targetType];
                    }
                    else if (targetType == BuiltInType.Byte && arrayDimensions > 0)
                    {
                        //Skip array information (UInt32) regarding its size
                        index += byteLength[BuiltInType.UInt32];
                        Int32[] tempArray = new Int32[arrayDimensions];
                        //Check every element of the array
                        for (int i = 0; i < arrayDimensions; i++)
                        {
                            tempArray[i] = byteResult[index];
                            index += byteLength[targetType];
                        }
                        //Add the values as one value seperated by ; to the data reference array
                        dataReferenceStringArray[2] = String.Join(";", tempArray);
                        dataReferenceStringArray[2] = String.Concat(dataReferenceStringArray[2], ";");
                    }
                    else if (!(arrayDimensions > 0))
                    {
                        //Get the converter method depending on the system type
                        System.Reflection.MethodInfo methodInfo = typeof(BitConverter).GetMethod("To"+systemType.Name);
                        //Call the method
                        dataReferenceStringArray[2] = methodInfo.Invoke(null, new Object[] { byteResult, index }).ToString();
                        index += byteLength[targetType];
                    }
                    else if (arrayDimensions > 0)
                    {
                        //Skip array information (UInt32) regarding its size
                        index += byteLength[BuiltInType.UInt32];
                        //Get the converter method depending on the system type
                        System.Reflection.MethodInfo methodInfo = typeof(BitConverter).GetMethod("To"+systemType.Name);
                        dynamic[] tempArray = new dynamic[arrayDimensions];
                        //Check every element of the array
                        for (int i = 0; i < arrayDimensions; i++)
                        {
                            tempArray[i] = methodInfo.Invoke(null, new Object[] { byteResult, index }).ToString();
                            index += byteLength[targetType];
                        }
                        //Add the values as one value seperated by ; to the data reference array
                        dataReferenceStringArray[2] = String.Join(";", tempArray);
                        dataReferenceStringArray[2] = String.Concat(dataReferenceStringArray[1], ";");
                    }
                    else
                    {
                        Exception e = new Exception("Data type is too complex to be parsed." + System.Environment.NewLine);
                        throw e;
                    }
                    //Copy the target type as string to the data reference array
                    dataReferenceStringArray[3] = targetType.ToString();
                    //Add the data Reference string array to the result string list
                    resultStringList.Add(dataReferenceStringArray);
                }
                //Count the array index
                arrayIndex +=1;
            }
            return resultStringList;
        }

        /// <summary>Browses for the desired type dictonary to parse for containing data types</summary>
        /// <param name="dataTypeNode">The data type node</param>
        /// <param name="theSessionToBrowseIn">The current session to browse in</param>
        /// <param name="structureDictionary">The list of string arrays containing the structure information tag name, data type id and array dimension</param>
        /// <returns>The dictionary as List of string arrays</returns>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        private static List<string[]> GetTypeDictionary(DataTypeNode dataTypeNode, Session theSessionToBrowseIn, List<string[]> structureDictionary)
        {
            //Create a temp list of string containing the structure dictionary
            List<string[]> tempStructureDictionary = new List<string[]>();
            tempStructureDictionary = structureDictionary;
            Session tempSession = theSessionToBrowseIn;
            //Get Structure Definition
            StructureFieldCollection structureFieldCollection = null;
            try
            {
                StructureDefinition structureDefinition = (StructureDefinition)dataTypeNode.DataTypeDefinition.Body;
                structureFieldCollection = (StructureFieldCollection)structureDefinition.Fields;
            }
            catch
            {
                structureDictionary[structureDictionary.Count - 1][1] = UAClientHelperAPI.GetParentDataType(dataTypeNode.NodeId, tempSession).ToString();
                return structureDictionary;
            }

            //Check every structure field in the collection
            foreach (StructureField structureField in structureFieldCollection)
            {
                if (structureField.DataType.NamespaceIndex != 0) //Check for structures in the structure
                {
                    //Create a reference description collection
                    ReferenceDescriptionCollection referenceDescriptionCollection;
                    //Create a continuationPoint
                    byte[] continuationPoint;
                    //Browse for the references of the data type of the structure field
                    theSessionToBrowseIn.Browse(
                                null,
                                null,
                                structureField.DataType,
                                0u,
                                BrowseDirection.Both,
                                ReferenceTypeIds.References,
                                true,
                                0,
                                out continuationPoint,
                                out referenceDescriptionCollection);
                    if (((NodeId)referenceDescriptionCollection[0].NodeId).NamespaceIndex == 0)//Data type is sub type of a base data type, i.e. BYTE is a sub tpye of Byte
                    {
                        //Check if the structure field is an array and parse the dimension to string
                        string arrayDimensions = "";
                        if (structureField.ValueRank == ValueRanks.OneDimension)
                        {
                            arrayDimensions = structureField.ArrayDimensions[0].ToString();
                        }
                        //Add the structure field name, its data type node id and array dimension to the dictionary list
                        tempStructureDictionary.Add(new string[] { structureField.Name, ((NodeId)referenceDescriptionCollection[0].NodeId).ToString(), arrayDimensions });
                    }
                    else//The structure/udt has at least one additional structure/udt inside
                    {
                        //Read the data type node id of the structure/udt in the parent structure/udt
                        DataTypeNode tempDataTypeNode = (DataTypeNode)theSessionToBrowseIn.ReadNode(structureField.DataType.ToString());
                        //Enter an empty Line to seperate the inside structure from the parent
                        tempStructureDictionary.Add(new string[] { structureField.Name + " - " + "struct/udt of type: " + tempDataTypeNode.DisplayName.ToString(), "", "" });
                        //Copy the temp dictionary to the real dictionary
                        structureDictionary = tempStructureDictionary;
                        //Recursively get the structure of the structure/udt in the parent structure/udt
                        GetTypeDictionary(tempDataTypeNode, tempSession, structureDictionary);
                    }
                }
                else //The structure field is a base data type
                {
                    //Check if the structure field is an array and parse the dimension to string
                    string arrayDimensions = "";
                    if (structureField.ValueRank == ValueRanks.OneDimension)
                    {
                        arrayDimensions = structureField.ArrayDimensions[0].ToString();
                    }
                    //Add the structure field name, its data type node id and array dimension to the dictionary list
                    tempStructureDictionary.Add(new string[] { structureField.Name, structureField.DataType.ToString(), arrayDimensions });
                }
                //Copy the temp dictionary to the real dictionary
                structureDictionary = tempStructureDictionary;
            }
            //Add a row for marking the end of the struct/udt

            structureDictionary.Add(new string[] { "/" + dataTypeNode.DisplayName.ToString(), "", "" });
            return structureDictionary;
        }

        /// <summary>Parses data to write to a list of byte arrays</summary>
        /// <param name="dataToWrite">The data to write as string[4]; string[0] = index, string[1] = tag name, string[2] = value, string[3] = opc data type</param>
        /// <param name="structureDictionary">The structure of the udt/struct</param>
        /// <returns>The list of the parsed byte arrays</returns>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        private static List<byte[]> ParseDataToByteArray(List<string[]> dataToWrite, List<string[]> structureDictionary)
        {
            //Define result byte list to return a byte array
            List<byte[]> resultByteList = new List<byte[]>();

            //Dictionary to get the right byte length depending on the data type
            Dictionary<BuiltInType, int> byteLength = new Dictionary<BuiltInType, int>()
                {
                    {BuiltInType.Boolean, 1},
                    {BuiltInType.Int16, 2},
                    {BuiltInType.Int32, 4},
                    {BuiltInType.Int64, 8},
                    {BuiltInType.UInt16, 2},
                    {BuiltInType.UInt32, 4},
                    {BuiltInType.UInt64, 8},
                    {BuiltInType.Float, 4},
                    {BuiltInType.Double, 8},
                    {BuiltInType.Byte, 1},
                    {BuiltInType.String, 4}
                };

            //Counter for the structure dictionary browsing
            int tempCounter = 0;

            //Create byte array to store the data of one array
            byte[] byteArray = new byte[0];

            //Start decoding for opc data types
            foreach (string[] element in dataToWrite)
            {
                //Reset the counter and the byteArray each time the counter reaches the size of the target Structure/UDT and the data to write include the target Structure/UDT several times
                if (tempCounter == structureDictionary.Count)
                {
                    //Add the byte array to the result byte array list
                    resultByteList.Add(byteArray);

                    //Reset the counter and the array
                    byteArray = new byte[0];
                    tempCounter = 0;
                }

                //Create byte array to store the bytes of one value depending on its data type, hence it is reseted to 0 for each element
                byte[] dataByteArray = new byte[0];

                //Get the data type id and the target type by using the structure dictionary
                NodeId datatypeId = new NodeId(structureDictionary[tempCounter][1]);
                BuiltInType targetType = TypeInfo.GetBuiltInType(datatypeId);

                //Get the system type for getting the rigth parse method
                Type systemType = TypeInfo.GetSystemType(datatypeId, null);

                //Parse array dimension string of the structure dictionary in int32
                Int32 arraySize = 0;
                if (structureDictionary[tempCounter][2] != "")
                {
                    arraySize = Int32.Parse(structureDictionary[tempCounter][2]);
                }

                //Serialize the byte string depending on the target data type and the array size - special if queries are needed for byte and string
                if (targetType == BuiltInType.String && !(arraySize  > 0))
                {
                    //Resize the data byte array to its correct size depending on the byte length of target type by using the dictionary
                    dataByteArray = new byte[byteLength[targetType]];
                    //Get the bytes of the string length and copy them to the beginning of the data byte array
                    Array.Copy(BitConverter.GetBytes(element[2].Length), 0, dataByteArray, 0, byteLength[targetType]);
                    //Convert the string to byte and concat them to the data byte array by converting every char of the string
                    foreach (Char c in element[2])
                    {
                        byte[] tempArray = new byte[1];
                        tempArray[0] = Convert.ToByte(c);
                        dataByteArray = dataByteArray.Concat(tempArray).ToArray();
                    }
                }
                else if (targetType == BuiltInType.String && arraySize  > 0)
                {
                    //Create a temp string array with the array size
                    string[] tempStringArr = new string[(arraySize-1)];
                    //Resize the data byte array to its correct size depending on the byte length of target type by using the dictionary
                    dataByteArray = new byte[byteLength[BuiltInType.UInt32]];
                    //Get the bytes of the string length and copy them to the beginning of the data byte array
                    Array.Copy(BitConverter.GetBytes(arraySize), 0, dataByteArray, 0, byteLength[targetType]);

                    //Get the single array elements by splitting the string
                    tempStringArr = element[2].Split(';');
                    //Go through every string element in the temp string array
                    for (int ii = 0; ii < arraySize; ii++)
                    {
                        //Copy the string length as byte to the data byte array
                        byte[] tempArray = new byte[byteLength[targetType]];
                        Array.Copy(BitConverter.GetBytes(tempStringArr[ii].Length), 0, tempArray, 0, byteLength[targetType]);
                        dataByteArray = dataByteArray.Concat(tempArray).ToArray();
                        //Convert the string to byte and concat them to the data byte array by converting every char of the string
                        foreach (Char c in tempStringArr[ii])
                        {
                            byte[] tempArraySingle = new byte[1];
                            tempArraySingle[0] = Convert.ToByte(c);
                            dataByteArray = dataByteArray.Concat(tempArraySingle).ToArray();
                        }
                    }
                }
                else if (targetType == BuiltInType.Byte && !(arraySize > 0))
                {
                    //Convert the string of byte to byte
                    dataByteArray = new byte[byteLength[targetType]];
                    dataByteArray[0] = Convert.ToByte(element[2]);
                }
                else if (targetType == BuiltInType.Byte && arraySize > 0)
                {
                    //Create a temp string variable to store the array elements which are divided by ;
                    String tempString = "";
                    //Resize the data byte array to its correct size depending on the byte length of target type by using the dictionary
                    dataByteArray = new byte[byteLength[BuiltInType.UInt32]];
                    //Get the bytes of the string length and copy them to the beginning of the data byte array
                    Array.Copy(BitConverter.GetBytes(arraySize), 0, dataByteArray, 0, byteLength[targetType]);

                    //Check every char in the string value of the element
                    foreach (Char c in element[2])
                    {
                        if (c != ';') //Concat every char to a string value in elements except the ;
                        {
                            tempString = String.Concat(tempString, c);
                        }
                        else
                        {
                            //Create a temp byte array with its correct size depending on the byte length of the target data type
                            byte[] tempArray = new byte[byteLength[targetType]];
                            tempArray[0] = Convert.ToByte(tempString);
                            //Copy the temp array to the data byte array
                            dataByteArray = dataByteArray.Concat(tempArray).ToArray();
                            //Reset the temp string
                            tempString = "";
                        }
                    }
                }
                else if (!(arraySize > 0))
                {
                    //Create the byte array with its correct size depending on the byte length of the target data type
                    dataByteArray = new byte[byteLength[targetType]];
                    //Get the converter method depending on the system type
                    System.Reflection.MethodInfo methodInfo = typeof(Convert).GetMethod("To"+systemType.Name, new Type[] { typeof(string) });
                    //Call the converter method
                    dynamic convertDataType = methodInfo.Invoke(null, new Object[] { element[2] });
                    Array.Copy(BitConverter.GetBytes(convertDataType), 0, dataByteArray, 0, byteLength[targetType]);
                }
                else if (arraySize > 0)
                {
                    //Create a temp string variable to store the array elements which are divided by ;
                    String tempString = "";
                    //Create the byte array with the size of a UInt32, which is the data type for the array dimension
                    dataByteArray= new byte[byteLength[BuiltInType.UInt32]];
                    //Get the bytes of the array dimension and copy them to the data byte array as they always are at the beginning of a byte array of an array
                    Array.Copy(BitConverter.GetBytes(arraySize), 0, dataByteArray, 0, byteLength[targetType]);

                    //Check every char in the string value of the element
                    foreach (Char c in element[2])
                    {
                        if (c != ';') //Concat every char to a string value in elements except the ;
                        {
                            tempString = String.Concat(tempString, c);
                        }
                        else
                        {
                            //Create a temp byte array with its correct size depending on the byte length of the target data type
                            byte[] tempArray = new byte[byteLength[targetType]];
                            //Get the converter method depending on the system type
                            System.Reflection.MethodInfo methodInfo = typeof(Convert).GetMethod("To"+systemType.Name, new Type[] { typeof(string) });
                            //Call the converter method
                            dynamic convertDataType = methodInfo.Invoke(null, new Object[] { element[2] });
                            Array.Copy(BitConverter.GetBytes(convertDataType), 0, tempArray, 0, byteLength[targetType]);
                            //Concat the temp array with the data array
                            dataByteArray = dataByteArray.Concat(tempArray).ToArray();
                            //Reset the temp string
                            tempString = "";
                        }
                    }
                }
                else
                {
                    Exception e = new Exception("Data type is too complex to be parsed." + System.Environment.NewLine);
                    throw e;
                }

                //Add the data byte array to the byte array containing all data and count the counter
                byteArray = byteArray.Concat(dataByteArray).ToArray();
                tempCounter++;
            }
            resultByteList.Add(byteArray);
            return resultByteList;
        }


        /// <summary>Browses for the node id of the parent data type of type base data type/summary>
        /// <param name="nodeId">The data to write as string[4]; string[0] = index, string[1] = tag name, string[2] = value, string[3] = opc data type</param>
        /// <returns>The node id of the parent data type</returns>
        /// <exception cref="Exception">Throws and forwards any exception with short error description.</exception>
        private static NodeId GetParentDataType(NodeId nodeId, Session theSessionToBrowseIn)
        {
            ReferenceDescriptionCollection referenceDescriptionCollection;
            byte[] continuationPoint;
            theSessionToBrowseIn.Browse(null, null, nodeId, 1, BrowseDirection.Inverse, ReferenceTypeIds.HasSubtype, true, 0, out continuationPoint, out referenceDescriptionCollection);
            NodeId nodeIdParentDataType = (NodeId)referenceDescriptionCollection[0].NodeId;

            if (nodeIdParentDataType.NamespaceIndex != 0)
            {
                nodeIdParentDataType = GetParentDataType(nodeIdParentDataType, theSessionToBrowseIn);
            }

            return nodeIdParentDataType;
        }
        #endregion
    }
}