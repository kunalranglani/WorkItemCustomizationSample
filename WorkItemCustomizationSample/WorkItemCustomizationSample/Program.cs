using System;
using System.Collections.Generic;
using System.Linq;
using CommandLine;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.Process.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.Process.WebApi.Models;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.WebApi;
using SimpleConsoleLogger;

namespace WorkItemCustomizationSample
{
    class Program
    {
        static void Main(string[] args)
        {
            var result = Parser.Default.ParseArguments<Options>(args);

            string accountUrl = null;
            string projectName = null;

            result.WithParsed((options) =>
            {
                accountUrl = options.AccountUrl;
                projectName = options.ProjectName;
            });

            result.WithNotParsed((e) =>
            {
                ConsoleLogger.LogError("Usage: WorkItemCustomizationSample.exe -a yourAccountUrl -p yourPojectName -c yourAreaPathName -g yourGroupName", true);
            });

            ConsoleLogger.Log("You might see a login screen if you have never signed in to your account using this app.");

            VssConnection connection = new VssConnection(new Uri(accountUrl), new VssClientCredentials());

            string workItemTypeName = "Task";
            string fieldName = "StringField";

            // todo add sample for picklist field

            ConsoleLogger.Log("Getting team project");
            // Get the team project
            TeamProject project = GetProject(connection, projectName);

            ConsoleLogger.Log($"Getting process for team project {project.Name}");
            Process process = GetProcess(connection, project);

            if (process.Type != ProcessType.Inherited)
            {
                ConsoleLogger.LogError("The process is not an inherited process.", true);
            }

            List<WorkItemTypeModel> workItemTypes = GetProcessWorkItemTypes(connection, process);

            if (!TryGetWorkItemType(workItemTypes, workItemTypeName, out WorkItemTypeModel workItemType))
            {
                ConsoleLogger.LogError("The work item type does not exist.", true);
            }

            string systemTypeRefName = null;
            string derivedTypeRefName = null;

            if (workItemType.Class == WorkItemTypeClass.Derived)
            {
                systemTypeRefName = workItemType.Inherits;
                derivedTypeRefName = workItemType.Id;
            }
            else
            {
                systemTypeRefName = workItemType.Id;
            }

            // since the derived type doesnt exists in the process. Lets add one.
            if (string.IsNullOrEmpty(derivedTypeRefName))
            {
                ConsoleLogger.Log("Derived work item type does not exit. Creating a new derived work item type");
                ProcessWorkItemType type = CreateWorkItemType(connection, process, workItemType);

                derivedTypeRefName = type.ReferenceName;
            }

            WorkItemField field = new WorkItemField()
            {
                Name = fieldName,
                Type = Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.FieldType.String
            };

            // add the field to the derived type
            var processWorkItemTypeField = AddFieldToWorkItemType(connection, field, process, derivedTypeRefName);

            ConsoleLogger.Log("Adding field as a control to the layout");
            // add the field as a control on the layout
            AddFieldToWorkItemTypeLayout(connection, process, processWorkItemTypeField, derivedTypeRefName);

            ConsoleLogger.LogSuccess("Field was successfully added to the work item type and layout");
        }

        private static TeamProject GetProject(VssConnection connection, string projectName)
        {
            ProjectHttpClient projectClient = connection.GetClient<ProjectHttpClient>();
            IEnumerable<TeamProjectReference> projects = projectClient.GetProjects(top: 10000).Result;

            TeamProjectReference project = projects.FirstOrDefault(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));

            return projectClient.GetProject(project.Id.ToString(), true).Result;
        }

        private static Process GetProcess(VssConnection connection, TeamProject project)
        {
            if (project.Capabilities.ContainsKey("processTemplate") && project.Capabilities["processTemplate"].ContainsKey("templateTypeId"))
            {
                var templateTypeId = Guid.Parse(project.Capabilities["processTemplate"]["templateTypeId"]);
                ProcessHttpClient processClient = connection.GetClient<ProcessHttpClient>();
                Process process = processClient.GetProcessByIdAsync(templateTypeId).Result;
                return process;
            }

            return null;
        }

        private static ProcessWorkItemType CreateWorkItemType(VssConnection connection, Process process, WorkItemTypeModel workItemType)
        {
            var model = new CreateProcessWorkItemTypeRequest()
            {
                Name = workItemType.Name,
                InheritsFrom = workItemType.Id,
                Color = workItemType.Color,
                Icon = workItemType.Icon,
                IsDisabled = workItemType.IsDisabled ?? false
            };

            WorkItemTrackingProcessHttpClient workClient = connection.GetClient<WorkItemTrackingProcessHttpClient>();

            return workClient.CreateProcessWorkItemTypeAsync(model, process.Id).Result;
        }

        private static WorkItemField CreateField(VssConnection connection, WorkItemField field, Process process)
        {
            var workClient = connection.GetClient<WorkItemTrackingHttpClient>();
            return workClient.CreateFieldAsync(field).Result;
        }

        private static PickList CreateList(VssConnection connection, PickList list)
        {
            var processDefinitionClient = connection.GetClient<WorkItemTrackingProcessHttpClient>();
            return processDefinitionClient.CreateListAsync(list).Result;
        }

        private static ProcessWorkItemTypeField AddFieldToWorkItemType(VssConnection connection, WorkItemField workItemField, string workItemTypeRefName, Process process)
        {
            var request = new AddProcessWorkItemTypeFieldRequest()
            {
                ReferenceName = workItemField.ReferenceName
            };

            var processClient = connection.GetClient<WorkItemTrackingProcessHttpClient>();
            return processClient.AddFieldToWorkItemTypeAsync(request, process.Id, workItemTypeRefName).Result;
        }

        private static bool TryGetWorkItemType(List<WorkItemTypeModel> types, string typeName, out WorkItemTypeModel type)
        {
            type = types.FirstOrDefault(x => x.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

            return type != null;
        }

        private static List<WorkItemTypeModel> GetProcessWorkItemTypes(VssConnection connection, Process process)
        {
            WorkItemTrackingProcessHttpClient workClient = connection.GetClient<WorkItemTrackingProcessHttpClient>();

            List<WorkItemTypeModel> types = workClient.GetWorkItemTypesAsync(process.Id).Result;

            return types;
        }

        private static WorkItemField GetField(VssConnection connection, string fieldName)
        {
            WorkItemTrackingHttpClient workClient = connection.GetClient<WorkItemTrackingHttpClient>();
            return workClient.GetFieldAsync(fieldName).Result;
        }

        private static ProcessWorkItemTypeField AddFieldToWorkItemType(VssConnection connection, WorkItemField field, Process process, string workItemTypeRefName)
        {
            // if field does not exist add field to process
            WorkItemField workItemField = null;

            try
            {
                workItemField = GetField(connection, field.Name);
                ConsoleLogger.Log($"Field {field.Name} already exists.");
            }
            catch (Exception e)
            {
            }

            if (workItemField == null)
            {
                workItemField = new WorkItemField()
                {
                    Name = field.Name,
                    Type = field.Type
                };

                ConsoleLogger.Log($"Field {field.Name} does not exist. Creating the field..");

                CreateField(connection, workItemField, process);
                workItemField = GetField(connection, field.Name);
            }

            ConsoleLogger.Log($"Adding field to the work item type");
            return AddFieldToWorkItemType(connection, workItemField, workItemTypeRefName, process);
        }

        private static void AddFieldToWorkItemTypeLayout(VssConnection connection, Process process, ProcessWorkItemTypeField field, string workItemTypeRefName)
        {
            FormLayout layout = GetLayout(connection, process, workItemTypeRefName);

            Group customGroup = null;

            // look for the custom group
            foreach (var page in layout.Pages)
            {
                foreach (var section in page.Sections)
                {
                    foreach (var group in section.Groups)
                    {
                        if (group.Label.Equals("custom", StringComparison.OrdinalIgnoreCase))
                        {
                            customGroup = group;
                            break;
                        }
                    }
                }
            }

            // create the group since it does not exist
            if (customGroup == null)
            {
                Group group = new Group()
                {
                    Label = "Custom",
                    Visible = true
                };

                var firstPage = layout.Pages[0];
                var lastSection = firstPage.Sections.LastOrDefault(x => x.Groups.Count > 0);

                ConsoleLogger.Log("Creating a group Custom to put the field control in");
                customGroup = CreateGroup(connection, group, process, workItemTypeRefName, firstPage.Id, lastSection.Id);
            }
            else
            {
                ConsoleLogger.Log("Layout group Custom already exists on the work item type");
            }

            // check if field already exists in the group
            Control fieldControl = null;
            foreach (var control in customGroup.Controls)
            {
                if (control.Id.Equals(field.ReferenceName, StringComparison.OrdinalIgnoreCase))
                {
                    fieldControl = control;
                    break;
                }
            }

            // add the field to the group
            if (fieldControl == null)
            {
                Control control = new Control()
                {
                    Id = field.ReferenceName,
                    ReadOnly = false,
                    Label = field.Name,
                    Visible = true
                };

                ConsoleLogger.Log("Adding the field control to the group");
                SetFieldInGroup(connection, control, process, workItemTypeRefName, customGroup.Id, field.ReferenceName);
            }
            else
            {
                ConsoleLogger.Log("Field already added to layout.");
            }
        }

        private static Control SetFieldInGroup(VssConnection connection, Control control, Process process, string witRefName, string groupId, string controlId)
        {
            var processDefinitionClient = connection.GetClient<WorkItemTrackingProcessHttpClient>();
            return processDefinitionClient.MoveControlToGroupAsync(control, process.Id, witRefName, groupId, controlId).Result;
        }

        private static FormLayout GetLayout(VssConnection connection, Process process, string witRefName)
        {
            var processDefinitionClient = connection.GetClient<WorkItemTrackingProcessHttpClient>();
            return processDefinitionClient.GetFormLayoutAsync(process.Id, witRefName).Result;
        }

        private static Group CreateGroup(VssConnection connection, Group group, Process process, string witRefName, string pageId, string sectionId)
        {
            var processDefinitionClient = connection.GetClient<WorkItemTrackingProcessHttpClient>();
            return processDefinitionClient.AddGroupAsync(group, process.Id, witRefName, pageId, sectionId).Result;
        }
    }
}
