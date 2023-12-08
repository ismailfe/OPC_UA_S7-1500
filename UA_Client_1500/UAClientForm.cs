//=============================================================================
// Siemens AG
// (c)Copyright (2018) All Rights Reserved
//----------------------------------------------------------------------------- 
// Tested with: Windows 10 Enterprise x64
// Engineering: Visual Studio 2013
// Functionality: Wrapps up important classes/methods of the OPC UA .NET Stack to help
// with simple client implementations
//-----------------------------------------------------------------------------
// Change log table:
// Version Date Expert in charge Changes applied
// 01.00.00 31.08.2016 (Siemens) First released version
// 01.01.00 22.02.2017 (Siemens) Implements user authentication, SHA256 Cert, Basic256Rsa256 connection,
// Basic256Rsa256 connections, read/write structs/UDTs
// 01.02.00 14.12.2017 (Siemens) Implements method calling
// 01.03.00 27.11.2018 (Siemens) Updated UAClientHelperAPI V1.4, Improved endpoint handling
//=============================================================================


using Opc.Ua;
using Opc.Ua.Client;
using Siemens.UAClientHelper;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Forms;

namespace UA_Client_1500
{
    public partial class UAClientForm : Form
    {
        /// <summary>
        /// Fields
        /// </summary>
        #region Fields
        private Session mySession;
        private Subscription mySubscription;
        private UAClientHelperAPI myClientHelperAPI;
        private EndpointDescription mySelectedEndpoint;
        private MonitoredItem myMonitoredItem;
        private List<String> myRegisteredNodeIdStrings;
        private ReferenceDescriptionCollection myReferenceDescriptionCollection;
        private List<string[]> myStructList;
        private UAClientCertForm myCertForm;
        private UAClientMatrixForm myMatrixForm;
        private Int16 itemCount;

        private Dictionary<NodeId, MonitoredItemNotification> mySubscribedItems;
        private List<string> lastMatrixInput;
        private List<string> rglastMatrixInput;
        #endregion

        /// <summary>
        /// Form Construction
        /// </summary>
        #region Construction
        public UAClientForm()
        {
            InitializeComponent();
            myClientHelperAPI = new UAClientHelperAPI();
            myRegisteredNodeIdStrings = new List<String>();
            browsePage.Enabled = false;
            rwPage.Enabled = false;
            subscribePage.Enabled = false;
            structPage.Enabled = false;
            methodPage.Enabled = false;
            itemCount = 0;
            mySubscribedItems = new Dictionary<NodeId, MonitoredItemNotification>();
            lastMatrixInput = new List<string>();
            rglastMatrixInput = new List<string>();
        }
        #endregion

        /// <summary>
        /// Event handlers called by the UI
        /// </summary>,
        #region UserInteractionHandlers
        private void EndpointButton_Click(object sender, EventArgs e)
        {
            bool foundEndpoints = false;
            endpointListView.Items.Clear();
            //The local discovery URL for the discovery server
            string discoveryUrl = discoveryTextBox.Text;
            try
            {
                ApplicationDescriptionCollection servers = myClientHelperAPI.FindServers(discoveryUrl);
                foreach (ApplicationDescription ad in servers)
                {
                    foreach (string url in ad.DiscoveryUrls)
                    {
                        try
                        {
                            EndpointDescriptionCollection endpoints = myClientHelperAPI.GetEndpoints(url);
                            foundEndpoints = foundEndpoints || endpoints.Count > 0;
                            foreach (EndpointDescription ep in endpoints)
                            {
                                string securityPolicy = ep.SecurityPolicyUri.Remove(0, 42);
                                string key = "[" + ad.ApplicationName + "] " + " [" + ep.SecurityMode + "] " + " [" + securityPolicy + "] " + " [" + ep.EndpointUrl + "]";
                                if (!endpointListView.Items.ContainsKey(key))
                                {
                                    endpointListView.Items.Add(key, key, 0).Tag = ep;
                                }

                            }
                        }
                        catch (ServiceResultException sre)
                        {
                            //If an url in ad.DiscoveryUrls can not be reached, myClientHelperAPI will throw an Exception
                            MessageBox.Show(sre.Message, "Error");
                        }

                    }
                    if (!foundEndpoints)
                    {
                        MessageBox.Show("Could not get any Endpoints", "Error");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error");
            }
        }
        private void SubscribeButton_Click(object sender, EventArgs e)
        {
            string subscriptionName = "TagSubscription";
            NodeId monitoredItemNodeId = new NodeId(subscriptionIdTextBox.Text);
            if (mySubscription == null)
            {
                try
                {
                    //use different item names for correct assignment at the notification event
                    itemCount++;
                    string monitoredItemName = "myItem" + itemCount.ToString();
                    mySubscription = myClientHelperAPI.Subscribe(1000, subscriptionName);
                    myMonitoredItem = myClientHelperAPI.AddMonitoredItem(mySubscription, subscriptionIdTextBox.Text, monitoredItemName, 1);
                    myClientHelperAPI.ItemChangedNotification += new MonitoredItemNotificationEventHandler(Notification_MonitoredItem);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error");
                }
            }
            else
            {
                List<MonitoredItem> monitoredItems = mySubscription.MonitoredItems.ToList();
                foreach (MonitoredItem monitoredItem in monitoredItems)
                {
                    if (monitoredItem.StartNodeId != monitoredItemNodeId)
                    {
                        try
                        {
                            //use different item names for correct assignment at the notification event
                            itemCount++;
                            string monitoredItemName = "myItem" + itemCount.ToString();
                            myMonitoredItem = myClientHelperAPI.AddMonitoredItem(mySubscription, subscriptionIdTextBox.Text, monitoredItemName, 1);
                            myClientHelperAPI.ItemChangedNotification += new MonitoredItemNotificationEventHandler(Notification_MonitoredItem);
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.Message, "Error");
                        }
                    }
                }
            }
        }
        private void EpConnectButton_Click(object sender, EventArgs e)
        {
            //Check if sessions exists; If yes > delete subscriptions and disconnect
            if (mySession != null && !mySession.Disposed)
            {
                try
                {
                    mySubscription.Delete(true);
                }
                catch
                {
                }

                myClientHelperAPI.Disconnect();
                mySession = myClientHelperAPI.Session;

                ResetUI();
            }
            else
            {
                try
                {
                    //Register mandatory events (cert and keep alive)
                    myClientHelperAPI.KeepAliveNotification += new KeepAliveEventHandler(Notification_KeepAlive);
                    myClientHelperAPI.CertificateValidationNotification += new CertificateValidationEventHandler(Notification_ServerCertificate);

                    //Check for a selected endpoint
                    if (mySelectedEndpoint != null)
                    {
                        //Call connect
                        myClientHelperAPI.Connect(mySelectedEndpoint, userPwButton.Checked, userTextBox.Text, pwTextBox.Text).Wait();
                        //Extract the session object for further direct session interactions
                        mySession = myClientHelperAPI.Session;

                        //UI settings
                        epConnectServerButton.Text = "Disconnect from selected endpoint";
                        browsePage.Enabled = true;
                        rwPage.Enabled = true;
                        subscribePage.Enabled = true;
                        structPage.Enabled = true;
                        methodPage.Enabled = true;
                        myCertForm = null;
                        myMatrixForm = null;
                    }
                    else
                    {
                        MessageBox.Show("Please select an endpoint before connecting", "Error");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    myCertForm = null;
                    myMatrixForm = null;
                    ResetUI();
                    MessageBox.Show(ex.InnerException.Message, "Error");
                }
            }

        }
        private void WriteValButton_Click(object sender, EventArgs e)
        {
            if (writeIdTextBox.Text == "")
            {
                MessageBox.Show("There is no nodeId entered", "Error");
            }
            else
            {
                NodeId nodeId = new NodeId(writeIdTextBox.Text);
                if (!GetArrayInformation(
                    nodeId,
                    out VariableNode variableNode,
                    out BuiltInType arraydataType,
                    out NodeId dataTypeId,
                    out uint arraySize,
                    out int valueRank))
                    return;

                var toWrite = new Dictionary<NodeId, IEnumerable<string>>();

                if (writeTextBox.Text == "" && !checkBox1.Checked && valueRank == ValueRanks.Scalar)
                {
                    MessageBox.Show("There is no value entered", "Error");
                    return;
                }
                else if (!lastMatrixInput.Any() && checkBox1.Checked && valueRank >= ValueRanks.OneDimension)
                {
                    MessageBox.Show("There is no value entered", "Error");
                    checkBox1.Checked = false;
                    return;
                }
                else if (!checkBox1.Checked && valueRank >= ValueRanks.OneDimension)
                {
                    MessageBox.Show($"The entered NodeId [{nodeId}] is an array or a matrix. Please check the checkbox.", "Error");
                    return;
                }
                else if (checkBox1.Checked && valueRank == ValueRanks.Scalar)
                {
                    MessageBox.Show($"The entered NodeId [{nodeId}] is a scalar. Please uncheck the checkbox.", "Error");
                    return;
                }
                else if (valueRank == ValueRanks.Scalar)
                {
                    toWrite.Add(nodeId, new List<string>() { writeTextBox.Text });
                }
                else //Written values are an array or matrix
                {
                    List<string> valueList = new List<string>();
                    foreach (string value in lastMatrixInput)
                    {
                        if (String.IsNullOrEmpty(value))
                        {
                            valueList.Add(TypeInfo.GetDefaultValue(dataTypeId, ValueRanks.Scalar).ToString());
                        }
                        else
                        {
                            valueList.Add(value);
                        }
                    }
                    toWrite.Add(nodeId, valueList);
                }
                try
                {
                    if (checkBox1.Checked)
                    {
                        checkBox1.Checked = false;
                    }
                    myClientHelperAPI.WriteValues(toWrite);
                    lastMatrixInput.Clear();
                    writeTextBox.Clear();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error");
                }
            }
        }
        private void writeIdTextBox_TextChanged(object sender, EventArgs e)
        {
            lastMatrixInput.Clear();
            writeTextBox.Clear();
            if (checkBox1.Checked)
            {
                checkBox1.Checked = false;
            }
        }
        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (writeIdTextBox.Text == "")
            {
                MessageBox.Show("There is no NodeId entered.");
                return;
            }
            if (checkBox1.Checked)
            {
                writeTextBox.Visible = false;
                label11.Visible = false;
                label15.Visible = true;
                NodeId nodeId = new NodeId(writeIdTextBox.Text);
                if (!GetArrayInformation(
                    nodeId,
                    out VariableNode variableNode,
                    out BuiltInType arraydataType,
                    out NodeId dataTypeId,
                    out uint arraySize,
                    out int valueRank))
                    return;
                if (valueRank >= ValueRanks.OneDimension)
                {
                    string[,] matrixArray = new string[arraySize, 3];
                    for (int i = 0; i <arraySize; i++)
                    {
                        GetMatrixIndeces(variableNode, arraySize, out string[] matrixIndexArray);
                        matrixArray[i, 0] = matrixIndexArray[i];
                        matrixArray[i, 1] = "";
                        matrixArray[i, 2] = arraydataType.ToString();
                    }
                    using (UAClientMatrixForm matrixDialog = new UAClientMatrixForm(matrixArray, sender))
                    {
                        if (matrixDialog.ShowDialog() == DialogResult.OK)
                        {
                            lastMatrixInput = matrixDialog.valuesToWrite;
                        }
                    }
                }
                else if (valueRank == ValueRanks.Scalar)
                {
                    checkBox1.Checked = false;
                    MessageBox.Show($"The entered NodeId [{nodeId}] is a scalar.", "Error");
                }
            }
            else
            {
                writeTextBox.Visible = true;
                label11.Visible = true;
                label15.Visible = false;
            }
        }

        private void UnsubscribeButton_Click(object sender, EventArgs e)
        {
            if (mySubscription != null)
            {
                myClientHelperAPI.RemoveSubscription(mySubscription);
                mySubscription = null;
                itemCount = 0;
                subscriptionTextBox.Text = "";
                mySubscribedItems.Clear();
            }
        }

        private void NodeTreeView_BeforeSelect(object sender, TreeViewCancelEventArgs e)
        {
            descriptionGridView.Rows.Clear();

            try
            {
                ReferenceDescription refDesc = (ReferenceDescription)e.Node.Tag;
                Node node = myClientHelperAPI.ReadNode(refDesc.NodeId.ToString());
                VariableNode variableNode = new VariableNode();

                string[] row1 = new string[] { "Node Id", refDesc.NodeId.ToString() };
                string[] row2 = new string[] { "Namespace Index", refDesc.NodeId.NamespaceIndex.ToString() };
                string[] row3 = new string[] { "Identifier Type", refDesc.NodeId.IdType.ToString() };
                string[] row4 = new string[] { "Identifier", refDesc.NodeId.Identifier.ToString() };
                string[] row5 = new string[] { "Browse Name", refDesc.BrowseName.ToString() };
                string[] row6 = new string[] { "Display Name", refDesc.DisplayName.ToString() };
                string[] row7 = new string[] { "Node Class", refDesc.NodeClass.ToString() };
                string[] row8 = new string[] { "Description", "null" };
                try { row8 = new string[] { "Description", node.Description.ToString() }; }
                catch { row8 = new string[] { "Description", "null" }; }
                string typeDefinition = "";
                if ((NodeId)refDesc.TypeDefinition.NamespaceIndex == 0)
                {
                    typeDefinition = refDesc.TypeDefinition.ToString();
                }
                else
                {
                    typeDefinition = "Struct/UDT: " + refDesc.TypeDefinition.ToString();
                }
                string[] row9 = new string[] { "Type Definition", typeDefinition };
                string[] row10 = new string[] { "Write Mask", node.WriteMask.ToString() };
                string[] row11 = new string[] { "User Write Mask", node.UserWriteMask.ToString() };
                if (node.NodeClass == NodeClass.Variable)
                {
                    variableNode = (VariableNode)node.DataLock;
                    List<NodeId> nodeIds = new List<NodeId>();
                    IList<string> displayNames = new List<string>();
                    IList<ServiceResult> errors = new List<ServiceResult>();
                    NodeId nodeId = new NodeId(variableNode.DataType);
                    nodeIds.Add(nodeId);
                    mySession.ReadDisplayName(nodeIds, out displayNames, out errors);
                    int valueRank = variableNode.ValueRank;
                    List<string> arrayDimension = new List<string>();

                    string[] row12 = new string[] { "Data Type", displayNames[0] };
                    string[] row13 = new string[] { "Value Rank", valueRank.ToString() };
                    //Define array dimensions depending on the value rank
                    if (valueRank > 0) //More dimensional arrays
                    {
                        for (int i = 0; i< valueRank; i++)
                        {
                            arrayDimension.Add(variableNode.ArrayDimensions.ElementAtOrDefault(i).ToString());
                        }
                    }
                    else
                    {
                        arrayDimension.Add("Scalar");
                    }
                    string[] row14 = new string[] { "Array Dimensions", String.Join(";", arrayDimension.ToArray()) };
                    string[] row15 = new string[] { "Access Level", variableNode.AccessLevel.ToString() };
                    string[] row16 = new string[] { "Minimum Sampling Interval", variableNode.MinimumSamplingInterval.ToString() };
                    string[] row17 = new string[] { "Historizing", variableNode.Historizing.ToString() };

                    object[] rows = new object[] { row1, row2, row3, row4, row5, row6, row7, row8, row9, row10, row11, row12, row13, row14, row15, row16, row17 };
                    foreach (string[] rowArray in rows)
                    {
                        descriptionGridView.Rows.Add(rowArray);
                    }
                }
                else
                {
                    object[] rows = new object[] { row1, row2, row3, row4, row5, row6, row7, row8, row9, row10, row11 };
                    foreach (string[] rowArray in rows)
                    {
                        descriptionGridView.Rows.Add(rowArray);
                    }
                }

                descriptionGridView.ClearSelection();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error");
            }

        }
        private void copyNodeId_Click(object sender, EventArgs e)
        {
            if (descriptionGridView.Rows.Count != 0)
            {
                try
                {
                    foreach (DataGridViewRow row in descriptionGridView.Rows)
                    {
                        if (row.Cells[0].Value.Equals("Node Id"))
                        {
                            Clipboard.SetText(row.Cells[1].Value.ToString());
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error");
                }
            }
            else
            {
                MessageBox.Show("Please select a node in the tree view.");
            }
        }
        private void ClientForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                myClientHelperAPI.Disconnect();
            }
            catch
            {
                ;
            }
        }
        private void NodeTreeView_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            e.Node.Nodes.Clear();

            ReferenceDescriptionCollection referenceDescriptionCollection;
            ReferenceDescription refDesc = (ReferenceDescription)e.Node.Tag;

            try
            {
                referenceDescriptionCollection = myClientHelperAPI.BrowseNode(refDesc);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error");
                return;
            }

            foreach (ReferenceDescription tempRefDesc in referenceDescriptionCollection)
            {
                if (tempRefDesc.ReferenceTypeId != ReferenceTypeIds.HasNotifier)
                {
                    e.Node.Nodes.Add(tempRefDesc.DisplayName.ToString()).Tag = tempRefDesc;
                }
            }
            //Add OPC images to the tree folder view
            foreach (TreeNode node in e.Node.Nodes)
            {
                node.Nodes.Add("");
                ReferenceDescription ref1 = (ReferenceDescription)node.Tag;
                //Dictionary storing the image names for the different NodeClasses
                Dictionary<NodeClass, string> imageIndexesForNodeClass = new Dictionary<NodeClass, string>()
                {
                    {NodeClass.Unspecified, "opc_warning"},
                    {NodeClass.DataType, "opc_datatype"},
                    {NodeClass.Object, "opc_object"},
                    {NodeClass.Variable, "opc_variable"},
                    {NodeClass.Method, "opc_method"},
                    {NodeClass.ObjectType, "opc_objecttype"},
                    {NodeClass.VariableType, "opc_variabletype"},
                    {NodeClass.ReferenceType, "opc_reftype"},
                    {NodeClass.View, "opc_view"}
                };
                string imagetype = ".png";
                //Define reference IDs for type definition folder type (61) and property type (68)
                ExpandedNodeId folderRefId = new ExpandedNodeId(61);
                ExpandedNodeId propertyRefId = new ExpandedNodeId(68);
                string FunctionalGroupType = "ns=2;i=1005";
                //Default image index set to number 10 - the treefolder image
                if (imageIndexesForNodeClass.ContainsKey(ref1.NodeClass) && imageList1.Images.Keys.Contains(imageIndexesForNodeClass[ref1.NodeClass]+imagetype))
                {
                    int index = 10;
                    string imagekey = "";
                    //NodeClass is object and hast the reference type "Folder" or "FunctionalGroupType"
                    if (ref1.TypeDefinition == folderRefId || ref1.TypeDefinition.ToString() == FunctionalGroupType)
                    {
                        imagekey = "opc_treefolder"+imagetype;
                    }

                    //NodeClass is variable and hast the reference type "Property"
                    else if (ref1.TypeDefinition == propertyRefId)
                    {
                        imagekey = "opc_property"+imagetype;
                    }
                    //NodeClass with type definition base data type
                    else
                    {
                        imagekey = imageIndexesForNodeClass[ref1.NodeClass]+imagetype;
                    }
                    index = imageList1.Images.IndexOfKey(imagekey);
                    node.ImageIndex = index;
                    node.SelectedImageIndex = index;
                }
            }
        }

        private void BrowsePage_Enter(object sender, EventArgs e)
        {
            if (myReferenceDescriptionCollection == null)
            {
                try
                {
                    myReferenceDescriptionCollection = myClientHelperAPI.BrowseRoot();
                    foreach (ReferenceDescription refDesc in myReferenceDescriptionCollection)
                    {
                        nodeTreeView.Nodes.Add(refDesc.DisplayName.ToString()).Tag = refDesc;
                        foreach (TreeNode node in nodeTreeView.Nodes)
                        {
                            node.Nodes.Add("");
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error");
                }
            }
        }
        private void ReadValButton_Click(object sender, EventArgs e)
        {
            NodeId nodeId = new NodeId(readIdTextBox.Text);
            if (!GetArrayInformation(
                nodeId,
                out VariableNode variableNode,
                out BuiltInType arraydataType,
                out NodeId dataTypeId,
                out uint arraySize,
                out int valueRank))
                return;

            List<String> nodeIdStrings = new List<String>();
            List<String> values = new List<String>();
            nodeIdStrings.Add(readIdTextBox.Text);

            try
            {
                values = myClientHelperAPI.ReadValues(nodeIdStrings);
                if (valueRank == ValueRanks.Scalar)
                {
                    readTextBox.Text = values.ElementAt<String>(0);
                }
                else //NodeId is a matrix
                {
                    String[] matrixValues = new string[arraySize];
                    matrixValues = values.ElementAt<String>(0).Split('\0');
                    string[,] matrixArray = new string[arraySize, 3];
                    GetMatrixIndeces(variableNode, arraySize, out string[] matrixIndexArray);
                    for (int i = 0; i <arraySize; i++)
                    {
                        matrixArray[i, 0] = matrixIndexArray[i];
                        matrixArray[i, 1] = matrixValues[i];
                        matrixArray[i, 2] = arraydataType.ToString();
                    }
                    myMatrixForm = new UAClientMatrixForm(matrixArray, sender);
                    readMatrixButton.Visible = true;
                    if (valueRank == ValueRanks.OneDimension)
                    {
                        string valuesArray = values.ElementAt<string>(0).Replace("\0", ";");
                        readTextBox.Text = valuesArray;
                    }
                    else
                    {
                        readTextBox.Text = "Click on the three dots to display the values.";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error");
            }
        }
        private void readMatrixButton_Click(object sender, EventArgs e)
        {
            myMatrixForm.ShowDialog();
        }
        private void readIdTextBox_TextChanged(object sender, EventArgs e)
        {
            readTextBox.Clear();
            readMatrixButton.Visible = false;
        }
        private void EndpointListView_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            mySelectedEndpoint = (EndpointDescription)e.Item.Tag;
        }
        private void OpcTabControl_Selecting(object sender, TabControlCancelEventArgs e)
        {
            e.Cancel = !e.TabPage.Enabled;
            if (!e.TabPage.Enabled)
            {
                MessageBox.Show("Establish a connection to a server first.", "Error");
            }
        }
        private void RegisterButton_Click(object sender, EventArgs e)
        {
            List<String> nodeIdStrings = new List<String>();
            nodeIdStrings.Add(rgNodeIdTextBox.Text);
            try
            {
                myRegisteredNodeIdStrings = myClientHelperAPI.RegisterNodeIds(nodeIdStrings);
                regNodeIdTextBox.Text = myRegisteredNodeIdStrings.ElementAt<String>(0);
                rgReadMatrixButton.Visible = false;
                rgReadTextBox.Clear();
                NodeId nodeId = new NodeId(regNodeIdTextBox.Text);
                GetArrayInformation(nodeId, out VariableNode variableNode, out BuiltInType arraydataType, out NodeId dataTypeId, out uint arraySize, out int valueRank);
                if (valueRank >= ValueRanks.OneDimension)
                {
                    label18.Visible = false;
                    rgWriteTextBox.Visible = false;
                    label23.Visible = true;
                    editValues.Visible = true;
                }
                else
                {
                    label18.Visible = true;
                    rgWriteTextBox.Visible = true;
                    label23.Visible = false;
                    editValues.Visible = false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error");
            }

        }
        private void UnregisterButton_Click(object sender, EventArgs e)
        {
            try
            {
                myClientHelperAPI.UnregisterNodeIds(myRegisteredNodeIdStrings);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error");
            }
            myRegisteredNodeIdStrings.Clear();
            regNodeIdTextBox.Text = "";
            rgReadTextBox.Text = "";
            rgWriteTextBox.Text = "";
            label18.Visible = true;
            rgWriteTextBox.Visible = true;
            label23.Visible = false;
            editValues.Visible = false;
            rgReadMatrixButton.Visible = false;
        }
        private void RgReadButton_Click(object sender, EventArgs e)
        {
            if (regNodeIdTextBox.Text == "")
            {
                MessageBox.Show("There is no nodeId registered.", "Error");
                return;
            }
            NodeId nodeId = new NodeId(regNodeIdTextBox.Text);
            if (!GetArrayInformation(
                nodeId,
                out VariableNode variableNode,
                out BuiltInType arraydataType,
                out NodeId dataTypeId,
                out uint arraySize,
                out int valueRank))
                return;

            List<String> nodeIdStrings = new List<String>();
            List<String> values = new List<String>();
            try
            {
                values = myClientHelperAPI.ReadValues(myRegisteredNodeIdStrings);
                if (valueRank == ValueRanks.Scalar)
                {
                    rgReadTextBox.Text = values.ElementAt<String>(0);
                }
                else //NodeId is a matrix
                {
                    String[] matrixValues = new string[arraySize];
                    matrixValues = values.ElementAt<String>(0).Split('\0');
                    string[,] matrixArray = new string[arraySize, 3];
                    GetMatrixIndeces(variableNode, arraySize, out string[] matrixIndexArray);
                    for (int i = 0; i <arraySize; i++)
                    {
                        matrixArray[i, 0] = matrixIndexArray[i];
                        matrixArray[i, 1] = matrixValues[i];
                        matrixArray[i, 2] = arraydataType.ToString();
                    }
                    myMatrixForm = new UAClientMatrixForm(matrixArray, sender);
                    rgReadMatrixButton.Visible = true;
                    if (valueRank == ValueRanks.OneDimension)
                    {
                        string valuesArray = values.ElementAt<string>(0).Replace("\0", ";");
                        rgReadTextBox.Text = valuesArray;
                    }
                    else
                    {
                        rgReadTextBox.Text = "Click on the three dots to display the values.";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error");
            }
        }
        private void rgReadMatrixButton_Click(object sender, EventArgs e)
        {
            myMatrixForm.ShowDialog();
        }
        private void RgWriteButton_Click(object sender, EventArgs e)
        {
            if (regNodeIdTextBox.Text == "")
            {
                MessageBox.Show("There is no nodeId registered.", "Error");
                return;
            }
            else
            {
                NodeId nodeId = new NodeId(regNodeIdTextBox.Text);
                if (!GetArrayInformation(
                    nodeId,
                    out VariableNode variableNode,
                    out BuiltInType arraydataType,
                    out NodeId dataTypeId,
                    out uint arraySize,
                    out int valueRank))
                    return;
                var toWrite = new Dictionary<NodeId, IEnumerable<string>>();
                if (rgWriteTextBox.Text == "" && valueRank == ValueRanks.Scalar)
                {
                    MessageBox.Show("There is no value entered", "Error");
                    return;
                }
                else if (!rglastMatrixInput.Any() && valueRank >= ValueRanks.OneDimension)
                {
                    MessageBox.Show("There is no value entered", "Error");
                    return;
                }
                else if (valueRank == ValueRanks.Scalar)
                {
                    toWrite.Add(nodeId, new List<string>() { rgWriteTextBox.Text });
                }
                else //Written values are an array or matrix
                {
                    List<string> valueList = new List<string>();
                    foreach (string value in rglastMatrixInput)
                    {
                        if (String.IsNullOrEmpty(value))
                        {
                            valueList.Add(TypeInfo.GetDefaultValue(dataTypeId, ValueRanks.Scalar).ToString());
                        }
                        else
                        {
                            valueList.Add(value);
                        }
                    }
                    toWrite.Add(nodeId, valueList);
                }
                try
                {
                    myClientHelperAPI.WriteValues(toWrite);
                    rglastMatrixInput.Clear();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error");
                }
            }

        }
        private void editValues_Click(object sender, EventArgs e)
        {
            NodeId nodeId = new NodeId(regNodeIdTextBox.Text);
            if (!GetArrayInformation(
                nodeId,
                out VariableNode variableNode,
                out BuiltInType arraydataType,
                out NodeId dataTypeId,
                out uint arraySize,
                out int valueRank))
                return;
            string[,] matrixArray = new string[arraySize, 3];
            for (int i = 0; i <arraySize; i++)
            {
                GetMatrixIndeces(variableNode, arraySize, out string[] matrixIndexArray);
                matrixArray[i, 0] = matrixIndexArray[i];
                matrixArray[i, 1] = "";
                matrixArray[i, 2] = arraydataType.ToString();
            }
            using (UAClientMatrixForm matrixDialog = new UAClientMatrixForm(matrixArray, sender))
            {
                if (matrixDialog.ShowDialog() == DialogResult.OK)
                {
                    rglastMatrixInput = matrixDialog.valuesToWrite;
                }
            }
        }
        private void UserPwButton_CheckedChanged(object sender, EventArgs e)
        {
            if (userPwButton.Checked)
            {
                userTextBox.Enabled = true;
                pwTextBox.Enabled = true;
            }
        }
        private void UserAnonButton_CheckedChanged(object sender, EventArgs e)
        {
            if (userAnonButton.Checked)
            {
                userTextBox.Enabled = false;
                pwTextBox.Enabled = false;
            }
        }
        private void StructReadButton_Click(object sender, EventArgs e)
        {
            structGridView.Rows.Clear();
            myStructList = new List<string[]>();
            string structNodeId = structNodeIdTextBox.Text;

            try
            {
                myStructList = myClientHelperAPI.ReadStructUdt(structNodeId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error");
                return;
            }
            Dictionary<uint, DataValue> attributes = myClientHelperAPI.ReadNodeAttributes(structNodeId);
            dynamic attributAccessLevel = attributes[Attributes.AccessLevel].Value;
            if (attributAccessLevel != 3)
            {
                structWriteButton.Visible = false;
            }
            else
            {
                structWriteButton.Visible = true;
            }
            foreach (string[] val in myStructList)
            {
                string[] row = new string[] { val[0], val[1], val[2], val[3] };
                if (val[3] == "")
                {
                    structGridView.Rows.Add(row);
                    structGridView.Rows[structGridView.Rows.Count - 1].Cells[0].Style.BackColor = Color.Gray;
                    structGridView.Rows[structGridView.Rows.Count - 1].Cells[1].Style.BackColor = Color.Gray;
                    structGridView.Rows[structGridView.Rows.Count - 1].Cells[2].Style.BackColor = Color.Gray;
                    structGridView.Rows[structGridView.Rows.Count - 1].Cells[3].Style.BackColor = Color.Gray;
                    structGridView.Rows[structGridView.Rows.Count - 1].Cells[2].ReadOnly = true;
                }
                else if (attributAccessLevel != 3)
                {
                    structGridView.Rows.Add(row);
                    structGridView.Rows[structGridView.Rows.Count - 1].Cells[2].Style.BackColor = Color.Gainsboro;
                    structGridView.Rows[structGridView.Rows.Count - 1].Cells[2].ReadOnly = true;
                }
                else
                {
                    structGridView.Rows.Add(row);
                }
            }
            structGridView.Rows.RemoveAt(structGridView.Rows.Count - 1);
            structGridView.ClearSelection();
        }
        private void StructWriteButton_Click(object sender, EventArgs e)
        {
            //Check if there are values
            if (structGridView.Rows.Count == 0)
            {
                MessageBox.Show("Read a struct/UDT first.", "Error");
                return;
            }

            //Clear the list and refill with values from GridView to get value changes
            myStructList.Clear();
            foreach (DataGridViewRow row in structGridView.Rows)
            {
                string dataType = row.Cells[3].Value.ToString();
                if (dataType == "")
                {
                    continue;
                }

                string[] tempString = new String[4];
                try
                {
                    tempString[0] = structGridView.Rows[row.Index].Cells[0].Value.ToString();
                }
                catch
                {
                    tempString[0] = "";
                }

                try
                {
                    tempString[1] = structGridView.Rows[row.Index].Cells[1].Value.ToString();
                }
                catch
                {
                    tempString[1] = "";
                }

                try
                {
                    tempString[2] = structGridView.Rows[row.Index].Cells[2].Value.ToString();
                }
                catch
                {
                    tempString[2] = "";
                }
                try
                {
                    tempString[3] = structGridView.Rows[row.Index].Cells[3].Value.ToString();
                }
                catch
                {
                    tempString[3] = "";
                }
                myStructList.Add(tempString);
            }

            try
            {
                myClientHelperAPI.WriteStructUdt(structNodeIdTextBox.Text, myStructList);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error");
            }
        }
        private void MethodInfoButton_Click(object sender, EventArgs e)
        {
            //Clear grid view first
            inputArgumentsGridView.Rows.Clear();
            outputArgumentsGridView.Rows.Clear();

            //Creata list of strings for the method's arguments
            List<string> methodArguments = new List<string>();

            //Get the arguments
            try
            {
                methodArguments = myClientHelperAPI.GetMethodArguments(methodNodeIdTextBox.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error");
            }

            //If argument is null there's no method
            if (methodArguments == null)
            {
                MessageBox.Show("The Node Id doesn't refer to a method", "Error");
                return;
            }

            //If no ObjectId was entetered
            if (string.IsNullOrEmpty(objectNodeIdTextBox.Text))
            {
                objectNodeIdTextBox.Text = myClientHelperAPI.GetParentNode(new NodeId(methodNodeIdTextBox.Text)).ToString();
            }

            //Check for display name to determine if there are intput and/or output arguments for the method
            foreach (String argument in methodArguments)
            {
                String[] strArray = argument.Split(';');

                if (strArray[0] == "InputArguments")
                {
                    string[] row = new string[] { strArray[1], strArray[2], strArray[3] };
                    inputArgumentsGridView.Rows.Add(row);
                }

                if (strArray[0] == "OutputArguments")
                {
                    string[] row = new string[] { strArray[1], strArray[2], strArray[3] };
                    outputArgumentsGridView.Rows.Add(row);
                }
            }

            //If there's no argument stored in the gridview there's no argument to care about
            if (inputArgumentsGridView.Rows.Count == 0)
            {
                string[] row = new string[] { "-", "-", "none" };
                inputArgumentsGridView.Rows.Add(row);
            }

            if (outputArgumentsGridView.Rows.Count == 0)
            {
                string[] row = new string[] { "-", "-", "none" };
                outputArgumentsGridView.Rows.Add(row);
            }

            inputArgumentsGridView.ClearSelection();
            outputArgumentsGridView.ClearSelection();

            //Enable the call button after retrieving argument info
            callButton.Enabled = true;
        }

        private void CallButton_Click(object sender, EventArgs e)
        {
            //Call the method

            //Create a list of string arrays for the input arguments
            List<string[]> inputData = new List<string[]>();
            //Object[] inputArguments = new object[inputArgumentsGridView.RowCount];
            List<object> inputArguments = new List<object>();

            //Copy data from the gridview to the argument list (value at [0]; data type at [1]) 
            //First check for data type "none" > no input argument available
            if (inputArgumentsGridView.Rows[0].Cells[2].Value.ToString() != "none")
            {
                foreach (DataGridViewRow row in inputArgumentsGridView.Rows)
                {
                    string value = row.Cells[1].Value.ToString();
                    //Check for missing input values
                    if (value == "")
                    {
                        MessageBox.Show("At least one input value is missing.", "Error");
                        return;
                    }
                    string dataType = row.Cells[2].Value.ToString();
                    inputData.Add(new String[2] { value, dataType });
                }
            }
            //Create an object list for retrieving the output arguments
            IList<object> outputValues;
            try
            {
                //Call the method
                outputValues = myClientHelperAPI.CallMethod(new NodeId(methodNodeIdTextBox.Text), new NodeId(objectNodeIdTextBox.Text), inputData);

                if (outputValues != null && outputValues.Count > 0)
                {
                    //Copy output arguments to the gridview
                    for (int i = 0; i < outputArgumentsGridView.Rows.Count; i++)
                    {
                        string outstring = "";
                        if (outputArgumentsGridView.Rows[i].Cells[2].Value.Equals("ByteString"))
                        {
                            outstring = BitConverter.ToString((byte[])outputValues[i]).Replace("-", string.Empty);
                        }
                        else
                        {
                            outstring = outputValues[i].ToString();
                        }

                        outputArgumentsGridView.Rows[i].Cells[1].Value = outstring;
                    }
                }
                //Success; Status = Good
                MessageBox.Show("Method called successfully.", "Success");
            }
            catch (Exception ex)
            {
                //Message contains status 
                MessageBox.Show(ex.Message, "Error");
            }
        }
        private void DiscoveryTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                EndpointButton_Click(this, new EventArgs());
            }
        }
        #endregion

        /// <summary>
        /// Global OPC UA event handlers
        /// </summary>
        #region OpcEventHandlers
        private void Notification_ServerCertificate(CertificateValidator cert, CertificateValidationEventArgs e)
        {
            //Handle certificate here
            //To accept a certificate manually move it to the root folder (Start > mmc.exe > add snap-in > certificates)
            //Or handle via UAClientCertForm

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new CertificateValidationEventHandler(Notification_ServerCertificate), cert, e);
                return;
            }

            try
            {
                //Search for the server's certificate in store; if found -> accept
                X509Store store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);
                X509CertificateCollection certCol = store.Certificates.Find(X509FindType.FindByThumbprint, e.Certificate.Thumbprint, true);
                store.Close();
                if (certCol.Capacity > 0)
                {
                    e.Accept = true;
                }

                //Show cert dialog if cert hasn't been accepted yet
                else
                {
                    if (!e.Accept & myCertForm == null)
                    {
                        myCertForm = new UAClientCertForm(e);
                        myCertForm.ShowDialog();
                    }
                }
            }
            catch
            {
                ;
            }
        }
        private void Notification_MonitoredItem(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            NodeId monitoredItemNodeId = monitoredItem.ResolvedNodeId;

            if (this.InvokeRequired)
            {
                this.BeginInvoke(new MonitoredItemNotificationEventHandler(Notification_MonitoredItem), monitoredItem, e);
                return;
            }
            MonitoredItemNotification notification = e.NotificationValue as MonitoredItemNotification;
            if (notification == null)
            {
                return;
            }
            if (!mySubscribedItems.ContainsKey(monitoredItemNodeId))
            {
                mySubscribedItems.Add(monitoredItemNodeId, notification);
            }
            else
            {
                mySubscribedItems[monitoredItemNodeId] = notification;
            }
            subscriptionTextBox.Text = "";
            int counter = 1;
            foreach (var valuePair in mySubscribedItems)
            {
                if (counter == 1)
                {
                    subscriptionTextBox.Text += "Item name: myItem" + counter.ToString();
                }
                else
                {
                    subscriptionTextBox.Text += Environment.NewLine + "Item name: myItem" + counter.ToString();
                }
                subscriptionTextBox.Text += Environment.NewLine + "Value: " + Utils.Format("{0}", valuePair.Value.Value.WrappedValue.ToString());
                subscriptionTextBox.Text += Environment.NewLine + "Source timestamp: " + valuePair.Value.Value.SourceTimestamp.ToString();
                subscriptionTextBox.Text += Environment.NewLine + "Server timestamp: " + valuePair.Value.Value.ServerTimestamp.ToString();
                subscriptionTextBox.Text += Environment.NewLine + "Status code: " + valuePair.Value.Value.StatusCode.ToString();
                subscriptionTextBox.Text += Environment.NewLine;
                counter +=1;
            }

        }
        private void Notification_KeepAlive(ISession sender, KeepAliveEventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new KeepAliveEventHandler(Notification_KeepAlive), sender, e);
                return;
            }

            try
            {
                // check for events from discarded sessions.
                if (!Object.ReferenceEquals(sender, mySession))
                {
                    return;
                }

                // check for disconnected session.
                if (!ServiceResult.IsGood(e.Status))
                {
                    // try reconnecting using the existing session state
                    mySession.Reconnect();
                }
            }
            catch
            {
                ResetUI();
                MessageBox.Show("Connection to OPC UA Server lost (possibly due to Server restart). Please connect again.", "Error");
            }
        }
        #endregion

        /// <summary>
        /// Private methods for UI handling
        /// </summary>
        #region PrivateMethods
        private void ResetUI()
        {
            descriptionGridView.Rows.Clear();
            nodeTreeView.Nodes.Clear();
            myReferenceDescriptionCollection = null;
            structGridView.Rows.Clear();
            inputArgumentsGridView.Rows.Clear();
            outputArgumentsGridView.Rows.Clear();
            myStructList = null;

            subscriptionTextBox.Text = "";
            subscriptionIdTextBox.Text = "";
            readIdTextBox.Text = "";
            writeIdTextBox.Text = "";
            readTextBox.Text = "";
            writeTextBox.Text = "";
            rgReadTextBox.Text = "";
            rgWriteTextBox.Text = "";
            rgNodeIdTextBox.Text = "";
            regNodeIdTextBox.Text = "";
            epConnectServerButton.Text = "Connect to selected endpoint";

            browsePage.Enabled = false;
            rwPage.Enabled = false;
            subscribePage.Enabled = false;
            structPage.Enabled = false;
            methodPage.Enabled = false;

            opcTabControl.SelectedIndex = 0;
        }

        private bool GetArrayInformation(NodeId nodeId, out VariableNode variableNode, out BuiltInType dataType, out NodeId dataTypeId, out uint arraylength, out int valueRank)
        {
            arraylength = 0;
            Node node = null;
            variableNode = null;
            dataType = BuiltInType.Null;
            dataTypeId = null;
            valueRank = 0;
            try
            {
                node = mySession.ReadNode(nodeId);
            }
            catch (ServiceResultException ex)
            {
                MessageBox.Show(string.Format("Reading values failed with following message: {0}", ex.Message), "Error");
                return false;
            }
            variableNode = (VariableNode)node.DataLock;
            dataType = TypeInfo.GetBuiltInType(variableNode.DataType);
            dataTypeId = variableNode.DataType;
            if (dataType == BuiltInType.Null)
            {
                try
                {
                    //Get the node id of the parent base data type
                    dataTypeId = GetParentDataType(dataTypeId, mySession);
                    dataType = TypeInfo.GetBuiltInType(dataTypeId);
                    if (dataType == BuiltInType.Null) //Ensure that the entered node id is not of type struct
                    {
                        MessageBox.Show("The entered node id may be of type struct. Please use the methods for read/write structs.", "Error");
                        return false;
                    }
                }
                catch
                {
                    MessageBox.Show("The node id data type is not castable it may be of type struct.", "Error");
                    return false;
                }
            }
            valueRank = variableNode.ValueRank;

            //Check if NodeId is  Matrix
            if (valueRank > ValueRanks.OneDimension)
            {
                arraylength = 1;
                for (int i = 0; i<valueRank; i++)
                {
                    arraylength *= variableNode.ArrayDimensions.ElementAtOrDefault(i);
                }
            }
            else //NodeId is Array
            {
                arraylength = variableNode.ArrayDimensions.ElementAtOrDefault(0);
            }
            return true;
        }

        private void GetMatrixIndeces(VariableNode variableNode, uint arraySize, out string[] matrixIndexArray)
        {
            matrixIndexArray = new string[arraySize];
            for (int i = 0; i <arraySize; i++)
            {
                List<long> indexes = new List<long>();
                long remainder = i;
                for (int j = variableNode.ArrayDimensions.Count-1; j >= 0; j--)
                {
                    uint dimension = variableNode.ArrayDimensions[j];
                    indexes.Insert(0, remainder % dimension);
                    remainder /= dimension;
                }
                String matrixIndex = "["+String.Join("][", indexes)+"]";
                matrixIndexArray[i] = matrixIndex;
            }
        }
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