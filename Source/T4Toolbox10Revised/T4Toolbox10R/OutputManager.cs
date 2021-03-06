﻿// <copyright file="OutputManager.cs" company="T4 Toolbox Team">
//  Copyright © T4 Toolbox Team. All Rights Reserved.
// </copyright>

namespace T4Toolbox
{
    using Microsoft.VisualStudio.TextTemplating;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;

    /// <summary>
    /// This class is a part of T4 Toolbox infrastructure. Don't use it in your code.
    /// </summary>
    /// <remarks>
    /// During code generation, TransformationContext calls <see cref="Append"/> 
    /// method of <see cref="OutputManager"/> to collect information about all generated
    /// outputs. In the end of code generation, the <see cref="UpdateFiles"/> method 
    /// is invoked to finish writing standard template output and code generation 
    /// log, if necessary. After that, the <see cref="UpdateFiles"/> method is invoked 
    /// on the default <see cref="AppDomain"/> with the help of <see cref="AppDomain.DoCallBack"/>
    /// method. 
    /// </remarks>
    [Serializable]
    public class OutputManager
    {
        #region fields

        /// <summary>
        /// Indicates whether standard output was created in the current session.
        /// </summary>
        [NonSerialized]
        private OutputInfo standardOutput;

        /// <summary>
        /// Stores information about output files generated by the current transformation.
        /// </summary>
        private List<OutputFile> outputFiles = new List<OutputFile>();

        #endregion

        /// <summary>
        /// Creates new or appends to existing output file.
        /// </summary>
        /// <param name="output">
        /// An <see cref="OutputInfo"/> object that describes content generated by a template.
        /// </param>
        /// <param name="content">
        /// A <see cref="String"/> that contains content generated by a template.
        /// </param>
        /// <param name="host">
        /// An <see cref="ITextTemplatingEngineHost"/> object hosting the <paramref name="transformation"/>.
        /// </param>
        /// <param name="transformation">
        /// <see cref="TextTransformation"/> object generated by T4 based on top-level .tt file.
        /// </param>
        /// <remarks>
        /// Multiple outputs can be combined in a single output file during code 
        /// generation. This allows user to customize a composite code generator 
        /// to generate output with required granularity without having to modify
        /// the generator itself.
        /// </remarks>
        public void Append(OutputInfo output, string content, ITextTemplatingEngineHost host, TextTransformation transformation)
        {
            if (output == null)
            {
                throw new ArgumentNullException("output");
            }

            if (host == null)
            {
                throw new ArgumentNullException("host");
            }

            string previousDirectory = Environment.CurrentDirectory;
            Environment.CurrentDirectory = Path.GetDirectoryName(host.TemplateFile);

            try
            {
                Validate(output);

                if (string.IsNullOrEmpty(output.File))
                {
                    this.AppendToStandardOutput(output, content, host, transformation);
                }
                else
                {
                    this.AppendToOutputFile(output, content, host);
                }
            }
            finally
            {
                Environment.CurrentDirectory = previousDirectory;
            }
        }

        /// <summary>
        /// Creates new and deletes old output files.
        /// </summary>
        /// <remarks>
        /// The <see cref="UpdateFiles"/> method must be executed in the default <see cref="AppDomain"/> 
        /// because automation objects for some Visual Studio project types don't inherit 
        /// from <see cref="MarshalByRefObject"/> and cannot be accessed via .NET remoting 
        /// from the templating <see cref="AppDomain"/>. In particular, the Database projects 
        /// of Visual Studio Team System cannot be accessed through application domain 
        /// boundries. As a workaround, we use this class to collect all required information 
        /// in the templating domain and call <see cref="AppDomain.DoCallBack"/> method to 
        /// marshal it to the default domain and <see cref="UpdateFiles"/> the files there.
        /// </remarks>
        public void UpdateFiles()
        {
            OutputProcessor.UpdateFiles(this.outputFiles);
        }

        #region private

        /// <summary>
        /// Determines absolute path to the output <see cref="OutputInfo.File"/>.
        /// </summary>
        /// <param name="output">
        /// An <see cref="OutputInfo"/> object that describes content generated by a template.
        /// </param>
        /// <param name="host">
        /// An <see cref="ITextTemplatingEngineHost"/> object hosting the <see cref="TextTransformation"/>.
        /// </param>
        /// <returns>
        /// If <see cref="OutputInfo.File"/> contains absolute path, returns its value. Otherwise,
        /// if <see cref="OutputInfo.Project"/> is specified, resolves relative <see cref="OutputInfo.File"/> 
        /// path based on the location of the project, otherwise resolves the path based on the location
        /// of the <see cref="ITextTemplatingEngineHost.TemplateFile"/>.
        /// </returns>
        private static string GetFullFilePath(OutputInfo output, ITextTemplatingEngineHost host)
        {
            // Does file contain absolute path already?
            if (Path.IsPathRooted(output.File))
            {
                return output.File;
            }

            // Resolve relative path
            string baseFile = string.IsNullOrEmpty(output.Project) ? host.TemplateFile : output.Project;
            string baseDirectory = Path.GetDirectoryName(baseFile);
            string fullPath = Path.Combine(baseDirectory, output.File);
            return Path.GetFullPath(fullPath);
        }

        /// <summary>
        /// Determines absolute path to the <see cref="OutputInfo.Project"/> file.
        /// </summary>
        /// <param name="output">
        /// An <see cref="OutputInfo"/> object that describes content generated by a template.
        /// </param>
        /// <returns>
        /// An absolute path to the <see cref="OutputInfo.Project"/>, if specified. Otherwise, returns 
        /// an empty string.
        /// </returns>
        private static string GetFullProjectPath(OutputInfo output)
        {
            if (string.IsNullOrEmpty(output.Project))
            {
                return string.Empty;
            }

            return Path.GetFullPath(output.Project);
        }

        /// <summary>
        /// Validates <paramref name="output"/> properties.
        /// </summary>
        /// <param name="output">
        /// An <see cref="OutputInfo"/> object that represents the generated output content.
        /// </param>
        private static void Validate(OutputInfo output)
        {
            // If content needs to be written to the standard output of the transformation
            if (string.IsNullOrEmpty(output.File))
            {
                if (!string.IsNullOrEmpty(output.Project))
                {
                    throw new TransformationException("Cannot move standard transformation output file");
                }

                if (!string.IsNullOrEmpty(output.BuildAction))
                {
                    throw new TransformationException("Cannot specify BuildAction for standard template output.");
                }

                if (output.CopyToOutputDirectory != CopyToOutputDirectory.DoNotCopy)
                {
                    throw new TransformationException("Cannot specify CopyToOutputDirectory for standard template output.");
                }

                if (output.CustomTool != null)
                {
                    throw new TransformationException("Cannot specify CustomTool for standard template output.");
                }

                if (output.CustomToolNamespace != null)
                {
                    throw new TransformationException("Cannot specify CustomToolNamespace for standard template output.");
                }

                if (output.BuildProperties.Count > 0)
                {
                    throw new TransformationException("Cannot specify BuildProperties for standard template output.");
                }

                if (output.PreserveExistingFile == true)
                {
                    throw new TransformationException("PreserveExistingFile cannot be set for standard template output");
                }
            }

            // If content needs to be written to another project
            if (!string.IsNullOrEmpty(output.Project))
            {
                // Make sure the project file exists
                string projectPath = GetFullProjectPath(output);
                if (!File.Exists(projectPath))
                {
                    throw new TransformationException(
                        string.Format(CultureInfo.CurrentCulture, "Target project {0} does not exist", projectPath));
                }
            }
        }

        /// <summary>
        /// Validates that <paramref name="output"/> properties are consistent 
        /// with properties of the <paramref name="previousOutput"/>.
        /// </summary>
        /// <param name="output">
        /// An <see cref="OutputInfo"/> object that represents the generated output content.
        /// </param>
        /// <param name="previousOutput">
        /// An <see cref="OutputInfo"/> object that represents previously generated output content.
        /// </param>
        private static void Validate(OutputInfo output, OutputInfo previousOutput)
        {
            if (!OutputInfo.SamePath(previousOutput.Project, GetFullProjectPath(output)))
            {
                throw new TransformationException("Project name doesn't match previously generated value");
            }

            if (previousOutput.Encoding != output.Encoding)
            {
                throw new TransformationException("Encoding doesn't match previously generated value");
            }

            if (previousOutput.BuildAction != output.BuildAction)
            {
                throw new TransformationException("Build action doesn't match previously generated value");
            }

            if (previousOutput.CopyToOutputDirectory != output.CopyToOutputDirectory)
            {
                throw new TransformationException("CopyToOutputDirectory doesn't match previously generated value");
            }

            if (previousOutput.CustomTool != output.CustomTool)
            {
                throw new TransformationException("CustomTool doesn't match previously generated value");
            }

            if (previousOutput.CustomToolNamespace != output.CustomToolNamespace)
            {
                throw new TransformationException("CustomToolNamespace doesn't match previously generated value");
            }

            foreach (KeyValuePair<string, string> buildProperty in previousOutput.BuildProperties)
            {
                string previousValue;
                bool propertyExists = output.BuildProperties.TryGetValue(buildProperty.Key, out previousValue);
                if (propertyExists && buildProperty.Value != previousValue)
                {
                    throw new TransformationException("Build property doesn't match previously generated value");
                }
            }

            if (previousOutput.PreserveExistingFile != output.PreserveExistingFile)
            {
                throw new TransformationException("PreserveExistingFile doesn't match previously generated value");
            }
        }

        /// <summary>
        /// Appends generated <paramref name="content"/> to the specified <see cref="OutputInfo.File"/>.
        /// </summary>
        /// <param name="output">
        /// An <see cref="OutputInfo"/> object that describes content generated by a template.
        /// </param>
        /// <param name="content">
        /// A <see cref="String"/> that contains content generated by a template.
        /// </param>
        /// <param name="host">
        /// An <see cref="ITextTemplatingEngineHost"/> object hosting the transformation.
        /// </param>
        private void AppendToOutputFile(OutputInfo output, string content, ITextTemplatingEngineHost host)
        {
            // If some content was already generated for this file 
            string filePath = GetFullFilePath(output, host);
            OutputFile outputFile = this.outputFiles.FirstOrDefault(o => OutputInfo.SamePath(o.File, filePath));
            if (outputFile != null)
            {
                // Verify that output properties match
                Validate(output, outputFile);
            }
            else
            {
                // Otherwise, create a new output container
                outputFile = new OutputFile();
                outputFile.File = filePath;
                outputFile.Project = GetFullProjectPath(output);
                outputFile.Encoding = output.Encoding;
                outputFile.BuildAction = output.BuildAction;
                outputFile.CustomTool = output.CustomTool;
                outputFile.CustomToolNamespace = output.CustomToolNamespace;
                outputFile.CopyToOutputDirectory = output.CopyToOutputDirectory;
                outputFile.PreserveExistingFile = output.PreserveExistingFile;
                this.outputFiles.Add(outputFile);
            }

            outputFile.Content.Append(content);
            outputFile.AppendBuildProperties(output.BuildProperties);
            outputFile.AppendReferences(output.References);
        }

        /// <summary>
        /// Appends generated <paramref name="content"/> to standard output of the <paramref name="transformation"/>.
        /// </summary>
        /// <param name="output">
        /// An <see cref="OutputInfo"/> object that describes content generated by a template.
        /// </param>
        /// <param name="content">
        /// A <see cref="String"/> that contains content generated by a template.
        /// </param>
        /// <param name="host">
        /// An <see cref="ITextTemplatingEngineHost"/> object hosting the <paramref name="transformation"/>.
        /// </param>
        /// <param name="transformation">
        /// <see cref="TextTransformation"/> object generated by T4 based on top-level .tt file.
        /// </param>
        private void AppendToStandardOutput(OutputInfo output, string content, ITextTemplatingEngineHost host, TextTransformation transformation)
        {
            // If some content was already written to the standard output
            if (this.standardOutput != null)
            {
                Validate(output, this.standardOutput);
            }

            transformation.Write(content);
            host.SetOutputEncoding(output.Encoding, false);

            if (this.standardOutput == null)
            {
                this.standardOutput = new OutputInfo();
                this.standardOutput.Project = GetFullProjectPath(output);
                this.standardOutput.Encoding = output.Encoding;
            }

            this.standardOutput.AppendReferences(output.References);
        }

        #endregion
    }
}
