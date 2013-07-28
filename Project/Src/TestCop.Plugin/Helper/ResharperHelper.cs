﻿// --
// -- TestCop http://testcop.codeplex.com
// -- License http://testcop.codeplex.com/license
// -- Copyright 2013
// --

using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Application;
using JetBrains.Application.DataContext;
using JetBrains.DataFlow;
using JetBrains.DocumentModel;
using JetBrains.IDE;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.LiveTemplates.FileTemplates;
using JetBrains.ReSharper.Feature.Services.Util;
using JetBrains.ReSharper.LiveTemplates.Templates;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Caches;
using JetBrains.ReSharper.Psi.Services;
using JetBrains.TextControl;
using JetBrains.Threading;
using JetBrains.UI;
using JetBrains.UI.PopupWindowManager;
using JetBrains.UI.RichText;
using JetBrains.UI.Tooltips;
using JetBrains.Util;
using TestCop.Plugin.Extensions;

namespace TestCop.Plugin.Helper
{
    static class ResharperHelper
    {
        public static void ForceKeyboardBindings()
        {
            const string conflictingMacroName = "Edit.GoTo";
            const string macroName = "Resharper_TestCop_JumpToTest";
            const string keyboardShortcut = "Global::Ctrl+G, Ctrl+T";

            if (DTEHelper.VisualStudioIsPresent())
            {
                ExecuteActionOnUiThread("force TestCop keyboard shortcut hack",
                                        () =>
                                            {
                                                DTEHelper.AssignKeyboardShortcutIfMissing(conflictingMacroName, "", "Global::Ctrl+G");
                                                DTEHelper.AssignKeyboardShortcutIfMissing(macroName, keyboardShortcut, null);
                                            });
            }
        }

        public static void AppendLineToOutputWindow(string msg)
        {
            if (DTEHelper.VisualStudioIsPresent())
            {
                ExecuteActionOnUiThread("testCop append text to output pane",
                                        () => DTEHelper.GetOutputWindowPane("TestCop", false).OutputString(msg + "\n"));
            }
        }

        public static Action ProtectActionFromReEntry(Lifetime lifetime, string name, Action fOnExecute)
        {
            System.Action fOnExecute2 = (System.Action)(() => IThreadingEx.ExecuteOrQueue(
                (IThreading)Shell.Instance.Locks, lifetime, name,()=> ReadLockCookie.Execute(fOnExecute) ));
            return fOnExecute2;
        }

        public static string UsingFileNameGetClassName(string baseFileName)
        {
            if (baseFileName.Contains("."))
            {
                //handles Class.DataAccessTests
                string className = baseFileName.Split(new[] { '.' })[0];
                return className;
            }
            return baseFileName;
        }
        
        public static string GetRelativeNameSpace(IProject project, IClrTypeName clrTypeClassName)
        {                        
            string targetNameSpace =
                clrTypeClassName.GetNamespaceName().RemoveLeading(project.GetDefaultNamespace()).RemoveLeading(".");

            return targetNameSpace;            
        }

        public static string GetBaseFileName(IDataContext context, ISolution solution)
        {            
            IProjectModelElement projectModelElement =
                context.GetData(JetBrains.ProjectModel.DataContext.DataConstants.PROJECT_MODEL_ELEMENT);

            var projectItem = projectModelElement as IProjectItem;
            if (projectItem == null) return null;


            FileSystemPath location = projectItem.Location;
            string fileName = location.NameWithoutExtension;  
                     
            return fileName;                        
        }
        
        public static IClrTypeName FindFirstTypeInFile(ISolution solution, IDocument document)
        {           
            for (int i = document.DocumentRange.StartOffset; i < document.DocumentRange.EndOffset; i++)
            {
                var firstTypeInFile = TextControlToPsi.GetContainingTypeOrTypeMember(solution, document, i) as ITypeElement;
                
                if (firstTypeInFile != null)
                {
                    AppendLineToOutputWindow("Hunted and found first name in file to be " + firstTypeInFile.GetClrName());
                    return firstTypeInFile.GetClrName();
                }
            }
            return null;
        }
        
        public static IClrTypeName GetClassNameAppropriateToLocation(ISolution solution, ITextControl textControl)
        {
            IDeclaredElement documentElement = TextControlToPsi.GetContainingTypeOrTypeMember(solution, textControl);
            if (documentElement == null)
            {
                return FindFirstTypeInFile(solution, textControl.Document);
            }

            AppendLineToOutputWindow("Element at cursor is of type " + documentElement.GetType().Name);

            IClrTypeName clrTypeName = null;

            if (documentElement is IClass)
            {
                clrTypeName = ((IClass)documentElement).GetClrName();
            }

            if (documentElement is ITypeElement && clrTypeName == null)
            {
                var containingType = ((ITypeElement)documentElement).GetContainingType();
                if (containingType != null) clrTypeName= containingType.GetClrName();                
            }
          
            if (clrTypeName == null)
            {
                AppendLineToOutputWindow("Unable to identify the class from current cursor position.");
                return FindFirstTypeInFile(solution, textControl.Document);
            }
            return clrTypeName;
        }
      
        public static void ShowTooltip(IDataContext context, ISolution solution, RichText tooltip)
        {
            var shellLocks = solution.GetComponent<IShellLocks>();
            var tooltipManager = solution.GetComponent<ITooltipManager>();

            tooltipManager.Show(tooltip,
              lifetime =>
              {
                  var windowContextSource = context.GetData(JetBrains.UI.DataConstants.PopupWindowContextSource);

                  if (windowContextSource != null)
                  {
                      var windowContext = windowContextSource.Create(lifetime);
                      var ctxTextControl = windowContext as TextControlPopupWindowContext;
                      return ctxTextControl == null ? windowContext :
                        ctxTextControl.OverrideLayouter(lifetime, lifetimeLayouter => new DockingLayouter(lifetimeLayouter, new TextControlAnchoringRect(lifetimeLayouter, ctxTextControl.TextControl, ctxTextControl.TextControl.Caret.Offset(), shellLocks), Anchoring2D.AnchorTopOrBottom));
                  }

                  return solution.GetComponent<MainWindowPopupWindowContext>().Create(lifetime);
              });
        }

        public static IProject FindAssociatedProject(IProject project)
        {
            return project.GetAssociatedProject();
        }

        public static void RemoveElementsNotInProject(List<IClrDeclaredElement> declaredElements, IProject associatedProject)
        {
            declaredElements.RemoveAll(p => p.GetSourceFiles().Any(de =>
            {
                var project = de.GetProject();
                return project != null &&
                       project != associatedProject;
            }));
        }
            
        public static List<IClrDeclaredElement> FindClass(ISolution solution, string classNameToFind, params IProject[] restrictToThisProjects)
        {
            #if R7
                        var declarationsCache = solution.GetPsiServices().CacheManager.GetDeclarationsCache(DeclarationCacheLibraryScope.NONE, false);
            #else     
                        var declarationsCache = solution.GetPsiServices().Symbols
                                            .GetSymbolScope(LibrarySymbolScope.FULL, false
                                            , solution.GetAllCodeProjects().First().GetResolveContext());
            #endif

            var results = declarationsCache.GetElementsByShortName(classNameToFind).ToList();

            foreach (var restrictToThisProject in restrictToThisProjects)
            {
                RemoveElementsNotInProject(results, restrictToThisProject);    
            }
                                                           
            return results;
        }
               
        private static void ExecuteActionOnUiThread(string description, Action fOnExecute)
        {
            var threading = Shell.Instance.GetComponent<IThreading>();
            threading.ReentrancyGuard.ExecuteOrQueueEx(description, fOnExecute);                        
        }

        public static void CreateFileWithinProject(Lifetime lifetime, IProject associatedProject,
                                                    FileSystemPath fileSystemPath, string targetFile)
        {
            var dataContexts = Shell.Instance.GetComponent<DataContexts>();
            var context = dataContexts.CreateOnActiveControl(lifetime);
                                
            Template classTemplate = FileTemplatesManager.Instance.GetFileTemplatesForActions(context).Where(c => c.Description == "Class").FirstOrDefault();
            if (classTemplate == null)
            {
                AppendLineToOutputWindow("Template for 'Class' not found");
            }
            IProjectFolder folder = (IProjectFolder) associatedProject.FindProjectItemByLocation(fileSystemPath)
                                    ?? AddNewItemUtil.GetOrCreateProjectFolder(associatedProject, fileSystemPath);

            if(folder==null)
            {
                AppendLineToOutputWindow("Error failed to create/location project folder"+fileSystemPath);                
                return;
            }
            
            string extension = Enumerable.First<string>(Shell.Instance.GetComponent<IProjectFileExtensions>().GetExtensions(associatedProject.ProjectProperties.DefaultLanguage.DefaultProjectFileType));
            IProjectFile newFile = FileTemplatesManager.Instance.CreateFileFromTemplate(targetFile + extension, folder, classTemplate);            
        }
    }    
}
