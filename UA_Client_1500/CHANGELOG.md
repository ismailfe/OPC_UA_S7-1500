Structure of changes:
<Version>	<Date>		<Expert in charge>
	<Changes applied>

changes:
01.00.00	31.08.2016	(Siemens)
	First released version
01.01.00	22.02.2017	(Siemens)
	Implements user authentication, SHA256 Cert, Basic256Rsa256 connection, read/write structs/UDTs
01.02.00	14.12.2017	(Siemens)
	Implements method calling
01.03.00	01.06.2018	(Siemens)
	Implements region Namespace; Improved parsing for complex data types; Minor bug fixes
01.04.00	27.11.2018	(Siemens)
	Update to OPC UA stack V1.04; Minor bug fixes
01.05.00	23.08.2022	(Siemens)
	- Update to OPC UA stack V1.04.369 (certificate creation changed, dependencies where added)
	- Add separate CHANGELOG file
	- Add opc.ua specific images for nodeclasses
	- Fill object id in call method env automatically and make it readonly
	- Rewrite the methods for read/write udts/structs
	- Restructure and simplify (i.e. casting the valus to the right data types) the read and the write method
	- Add a matrix/array view for matrix/array nodeIds and update the gui accordingly
	- Rewrite the subscription method to be able to subscribe to more than one node id
01.05.00	23.05.2023	(Siemens)
	- Update to OPC UA stack V1.04.371
