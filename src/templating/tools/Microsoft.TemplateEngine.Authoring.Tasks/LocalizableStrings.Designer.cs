﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Microsoft.TemplateEngine.Authoring.Tasks {
    using System;
    
    
    /// <summary>
    ///   A strongly-typed resource class, for looking up localized strings, etc.
    /// </summary>
    // This class was auto-generated by the StronglyTypedResourceBuilder
    // class via a tool like ResGen or Visual Studio.
    // To add or remove a member, edit your .ResX file then rerun ResGen
    // with the /str option, or rebuild your VS project.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "17.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    internal class LocalizableStrings {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal LocalizableStrings() {
        }
        
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("Microsoft.TemplateEngine.Authoring.Tasks.LocalizableStrings", typeof(LocalizableStrings).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Failed to export localization files for &apos;template.json&apos; file under the path &apos;{0}&apos;: {1}..
        /// </summary>
        internal static string Command_Localize_Log_ExportTaskFailed {
            get {
                return ResourceManager.GetString("Command_Localize_Log_ExportTaskFailed", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to &apos;LocalizeTemplates&apos; stopped processing the following file, because the operation was cancelled: {0}.
        /// </summary>
        internal static string Command_Localize_Log_FileProcessingCancelled {
            get {
                return ResourceManager.GetString("Command_Localize_Log_FileProcessingCancelled", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The property &apos;TemplateFolder&apos; should be set for &apos;LocalizeTemplates&apos; target..
        /// </summary>
        internal static string Command_Localize_Log_TemplateFolderNotSet {
            get {
                return ResourceManager.GetString("Command_Localize_Log_TemplateFolderNotSet", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Failed to find &apos;template.json&apos; file under the path &apos;{0}&apos;..
        /// </summary>
        internal static string Command_Localize_Log_TemplateJsonNotFound {
            get {
                return ResourceManager.GetString("Command_Localize_Log_TemplateJsonNotFound", resourceCulture);
            }
        }
    }
}
