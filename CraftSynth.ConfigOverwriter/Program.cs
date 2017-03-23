﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;

namespace CraftSynth.ConfigOverwriter
{
	/// <summary>
	/// Overwrites .config with content from .config.base and than injects items from .config.localparts
	/// Watch video at http://www.f4cio.com/ConfigOverwritter.aspx 
	/// 
	/// To use it without NuGet put next line in pre-build event. If installed via NuGet it will be called on pre-build differently so this line should not be used.
	/// $(SolutionDir)CraftSynth.ConfigOverwriter\bin\Debug\CraftSynth.ConfigOverwriter.exe "$(ProjectDir)web.config.base" "$(ProjectDir)web.config.localparts" "$(ProjectDir)web.config"
	/// </summary>
	class Program
	{
		private const string TIMESTAMP_PRETEXT = "Autogenerated. See http://www.f4cio.com/ConfigOverwritter.aspx ";
		
		static void Main(string[] args)
		{
			try
			{
				if (args.Length != 3)
				{
					throw new Exception("App must be run with three arguments (three file paths to: .config.base,  .config.localparts and  .config).");
				}

				string baseConfigFilePath = args[0];
				if (!File.Exists(baseConfigFilePath))
				{
					throw new Exception("base File not found:" + baseConfigFilePath);
				}

				string localpartsConfigFilePath = args[1];		
				string finalConfigFilePath = args[2];

				#region on very first run .base will be empty - fill it with all from final config 
				if (new FileInfo(baseConfigFilePath).Length == 0 && File.Exists(finalConfigFilePath))
				{
					string t = File.ReadAllText(finalConfigFilePath);
					File.WriteAllText(baseConfigFilePath, t);
				}
				#endregion

				#region after cloning from source control to new location or machine .base will be non-empty and both web.config and web.config.localparts will not exist - create empty web.config.localparts. web.config will be rebuilt later 
				if (new FileInfo(baseConfigFilePath).Length > 0 && !File.Exists(localpartsConfigFilePath))
				{
					string t = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<configuration>\r\n</configuration>";
					File.WriteAllText(localpartsConfigFilePath, t);
				}
				#endregion

				if (!File.Exists(localpartsConfigFilePath))
				{
					throw new Exception("localparts File not found:" + localpartsConfigFilePath);
				}

				#region first .config.base --(overwrite)--> .config but only if .config.base changed!
				bool shouldOverwriteFinalWithBase;
					DateTime? timestamp1 = GetTimestamp(finalConfigFilePath, 0);
					if (timestamp1 == null)
					{//no or invalid timestamp
						shouldOverwriteFinalWithBase = true;
					}
					else
					{//valid timestamp -compare 
						if (File.GetLastWriteTimeUtc(baseConfigFilePath).CompareTo(timestamp1) > 0)
						{
							shouldOverwriteFinalWithBase = true;
						}
						else
						{
							shouldOverwriteFinalWithBase = false;
						}
					}
				

				DateTime? firstTimestamp = null; 
				if (shouldOverwriteFinalWithBase)
				{
					File.Copy(baseConfigFilePath, finalConfigFilePath, true);
					firstTimestamp = DateTime.UtcNow;
				}
				if (!File.Exists(finalConfigFilePath))
				{
					throw new Exception("final File not found:" + finalConfigFilePath);
				}
				#endregion

				DateTime? secondTimestamp = null;
				if (shouldOverwriteFinalWithBase || File.GetLastWriteTimeUtc(localpartsConfigFilePath).CompareTo(GetTimestamp(finalConfigFilePath,1)??DateTime.MinValue) > 0)
				{
					localpartsXmlDoc = new XmlDocument();
					localpartsXmlDoc.Load(localpartsConfigFilePath);

					finalXmlDoc = new XmlDocument();
					finalXmlDoc.Load(finalConfigFilePath);

					foreach (XmlNode node in localpartsXmlDoc.SelectNodes("/"))
					{
						ProcessNode(node);
					}

					finalXmlDoc.Save(finalConfigFilePath);
					secondTimestamp = DateTime.UtcNow;
				}

				if (firstTimestamp != null || secondTimestamp != null)
				{
					UpdateTimestamps(finalConfigFilePath, firstTimestamp, secondTimestamp);
				}
			}
			catch (Exception exception)
			{
				try
				{
					string exeFolderPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase).Replace("file:\\","");
					string content = exception.Message + "\r\n\r\n" + exception.StackTrace;
					File.WriteAllText(exeFolderPath + "\\LastError.txt", content);
				}
				catch (Exception)
				{
				}
				throw exception;
			}

		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="finalConfigFilePath"></param>
		/// <param name="index"></param>
		/// <returns></returns>
		private static DateTime? GetTimestamp(string finalConfigFilePath, int index)
		{
			DateTime? r = null;

			try
			{
				XmlNode timestampNode = GetTimestampNode(finalConfigFilePath);

				if (timestampNode != null && timestampNode.InnerText != null)
				{
					string[] innerTextParts = timestampNode.InnerText.Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
					
					
					r = ParseDateAndTimeAs_YYYY_MM_DD__HH_MM_SS(innerTextParts[index+2]);
					r = DateTime.SpecifyKind(r.Value, DateTimeKind.Utc);
				}
			}
			catch (Exception)
			{
				r = null;
			}


			return r;
		}

		private static XmlNode GetTimestampNode(string finalConfigFilePath)
		{
			finalXmlDoc = new XmlDocument();
			finalXmlDoc.Load(finalConfigFilePath);
			finalXmlDoc.SelectNodes("/");
			XmlNode timestampNode = null;  
			foreach (XmlNode n in finalXmlDoc.SelectSingleNode("/").ChildNodes)
			{
				if (n.NodeType == XmlNodeType.Comment && n.InnerText!=null && n.InnerText.StartsWith(TIMESTAMP_PRETEXT))
				{
					timestampNode = n;
					break;
				}
			}
			if (timestampNode == null)
			{
				timestampNode = finalXmlDoc.CreateComment(string.Empty);
				finalXmlDoc.InsertAfter(timestampNode, finalXmlDoc.ChildNodes[0]);
			}
			else
			{
				if (!timestampNode.InnerText.StartsWith(TIMESTAMP_PRETEXT))
				{
					timestampNode.InnerText = string.Empty;
				}
			}
			return timestampNode;
		}

		private static void UpdateTimestamps(string finalConfigFilePath, DateTime? timestamp1, DateTime? timestamp2)
		{
			if (timestamp1 == null)
			{
				timestamp1 = GetTimestamp(finalConfigFilePath, 0);
			}
			if (timestamp1 == null)
			{
				timestamp1 = DateTime.MinValue;
			}

			if (timestamp2 == null)
			{
				timestamp2 = GetTimestamp(finalConfigFilePath, 1);
			}
			if (timestamp2 == null)
			{
				timestamp2 = DateTime.MinValue;
			}


			string timeStamp = TIMESTAMP_PRETEXT+"|UTC|" + ToDDateAndTimeAs_YYYY_MM_DD__HH_MM_SS(timestamp1.Value)+"|"+ToDDateAndTimeAs_YYYY_MM_DD__HH_MM_SS(timestamp2.Value);
			XmlNode timestampNode = GetTimestampNode(finalConfigFilePath);
			timestampNode.InnerText = timeStamp;

			finalXmlDoc.Save(finalConfigFilePath);
		}

		public static string ToDDateAndTimeAs_YYYY_MM_DD__HH_MM_SS(DateTime now)
		{
			string currentDateAndTimeSortable =
				String.Format("{0}.{1}.{2}. {3}-{4}-{5}",
				now.Year,
				now.Month.ToString("00"),
				now.Day.ToString("00"),
				now.Hour.ToString("00"),
				now.Minute.ToString("00"),
				now.Second.ToString("00")
				);
			return currentDateAndTimeSortable;
		}

		public static DateTime? ParseDateAndTimeAs_YYYY_MM_DD__HH_MM_SS(string s, bool allowCharsAfterDate = false)
		{
			DateTime? r;
			try
			{
				//2014.06.31 24-59-59
				//0123456789012345678
				if (!allowCharsAfterDate && s.Length != 12)
				{
					throw new Exception("Invalid length.");
				}

				int year = int.Parse(s.Substring(0, 4));
				int month = int.Parse(s.Substring(5, 2));
				int day = int.Parse(s.Substring(8, 2));
				int hour = int.Parse(s.Substring(11, 2));
				int minute = int.Parse(s.Substring(17, 2));
				int second = int.Parse(s.Substring(17, 2));
				r = new DateTime(year, month, day, hour, minute, second);
			}
			catch (Exception)
			{
				r = null;
			}
			return r;
		}

		private static XmlDocument localpartsXmlDoc;
		private static XmlDocument finalXmlDoc;

		private static void ProcessNode(XmlNode node)
		{
			if (node.Attributes!=null && node.Attributes["overwriteChildsMatchedBy"]!=null && node.Attributes["overwriteChildsMatchedBy"].InnerText == "*")
			{
				string xPath = GetXPath(node);
				XmlNode targetNode = BuildXmlNodeDeep(finalXmlDoc, xPath);
				//delete all old children
				if(targetNode.ChildNodes!=null)
				{
					while (targetNode.ChildNodes.Count>0)
					{
						targetNode.RemoveChild(targetNode.ChildNodes[0]);
					}
				}
				//add new from source
				foreach (XmlNode childToAdd in node.ChildNodes)
				{
					XmlNode importedNode = finalXmlDoc.ImportNode(childToAdd, true);
					targetNode.AppendChild(importedNode);
				}
			}
			else if (node.Attributes != null && node.Attributes["overwriteChildsMatchedBy"] != null && node.Attributes["overwriteChildsMatchedBy"].InnerText != "*")
			{
				string attrName = node.Attributes["overwriteChildsMatchedBy"].InnerText;
				string xPath = GetXPath(node);
				XmlNode targetNode = BuildXmlNodeDeep(finalXmlDoc, xPath);

				XmlNode lastInsertedNode = null;

				foreach (XmlNode childToAdd in node.ChildNodes)
				{
						XmlNode importedNode = finalXmlDoc.ImportNode(childToAdd, true);

						if (importedNode.Name != "#comment")
						{
							bool inserted = false;

							for (int i = targetNode.ChildNodes.Count - 1; i >= 0; i--)
							{
								XmlNode oldNode = targetNode.ChildNodes[i];
								if (attrName.ToLower().StartsWith("tagname="))
								{
									string tagName = attrName.Split('=')[1];
									if (oldNode.Name == tagName)
									{
										targetNode.InsertAfter(importedNode, oldNode);
										targetNode.RemoveChild(oldNode);
										inserted = true;
										lastInsertedNode = importedNode;
									}
								}
								else
								{
									if (oldNode.Attributes != null &&
									    oldNode.Attributes[attrName] != null &&
									    IsNOTNullOrWhiteSpace(oldNode.Attributes[attrName].Value) &&

									    childToAdd.Attributes != null &&
									    childToAdd.Attributes[attrName] != null &&
									    IsNOTNullOrWhiteSpace(childToAdd.Attributes[attrName].Value) &&

									    oldNode.Attributes[attrName].Value == childToAdd.Attributes[attrName].Value)
									{
										targetNode.InsertAfter(importedNode, oldNode);
										targetNode.RemoveChild(oldNode);
										inserted = true;
										lastInsertedNode = importedNode;
									}
								}
							}

							if (!inserted)
							{

								if (lastInsertedNode == null)
								{
									targetNode.AppendChild(importedNode);
								}
								else
								{
									targetNode.InsertAfter(importedNode, lastInsertedNode);
								}
							}
						}
				}
			}
			else
			{
				if (node.ChildNodes != null && node.ChildNodes.Count > 0)
				{
					foreach (XmlNode child in node.ChildNodes)
					{
						ProcessNode(child);
					}
				}
			}

			
		}

		public static bool IsNOTNullOrWhiteSpace(string nullableObject)
		{
			return (nullableObject??"").Trim().Length > 0;
		}


		public static XmlNode BuildXmlNodeDeep(XmlDocument xmlDocument, string xPath)
		{
			string[] ancestorsNames = xPath.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
			XmlNode currentXmlNode = xmlDocument;
			string currentXmlNodeXPath = string.Empty;
			foreach (var ancestorName in ancestorsNames)
			{
				currentXmlNodeXPath += "/" + ancestorName;
				if (currentXmlNode.SelectNodes(ancestorName) == null || currentXmlNode.SelectNodes(ancestorName).Count == 0)
				{
					XmlNode previousSibling = null;
					XmlNode previousSiblingFromSourceDoc = localpartsXmlDoc.SelectSingleNode(currentXmlNodeXPath).PreviousSibling;
					if(previousSiblingFromSourceDoc!=null)
					{
						string xPathOfpreviousSiblingFromSourceDoc = GetXPath(previousSiblingFromSourceDoc);
						previousSibling = xmlDocument.SelectSingleNode(xPathOfpreviousSiblingFromSourceDoc);
					}

					if (previousSibling == null)
					{
						currentXmlNode.AppendChild(xmlDocument.CreateElement(ancestorName));
					}
					else
					{
						currentXmlNode.InsertAfter(xmlDocument.CreateElement(ancestorName),previousSibling);
					}
				}
				currentXmlNode = currentXmlNode.SelectNodes(ancestorName)[0];
			}

			return currentXmlNode;
		}

		public static string GetXPath(XmlNode node)
		{
			StringBuilder builder = new StringBuilder();
			while (node != null)
			{
				switch (node.NodeType)
				{
					case XmlNodeType.Attribute:
						builder.Insert(0, "/@" + node.Name);
						node = ((XmlAttribute)node).OwnerElement;
						break;
					case XmlNodeType.Element:
						//int index = FindElementIndex((XmlElement)node);
						builder.Insert(0, "/" + node.Name/* + "[" + index + "]"*/);
						node = node.ParentNode;
						break;
					case XmlNodeType.Document:
						return builder.ToString();
					default:
						throw new ArgumentException("Only elements and attributes are supported");
				}
			}
			throw new ArgumentException("Node was not in a document");
		}
	}
}
