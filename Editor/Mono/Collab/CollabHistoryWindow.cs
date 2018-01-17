// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor.Collaboration;
using UnityEditor.Experimental.UIElements;
using UnityEngine.Experimental.UIElements;
using UnityEngine.Experimental.UIElements.StyleEnums;
using UnityEngine;
using UnityScript.Steps;
using UnityEditor.Connect;

namespace UnityEditor
{
    internal class CollabHistoryWindow : EditorWindow, ICollabHistoryWindow
    {
        const string kWindowTitle = "Collab History";
        const string kServiceUrl = "developer.cloud.unity3d.com";

        [MenuItem("Window/Collab History", false, 2011)]
        public static void ShowHistoryWindow()
        {
            EditorWindow.GetWindow<CollabHistoryWindow>(kWindowTitle);
        }

        [MenuItem("Window/Collab History", true)]
        public static bool ValidateShowHistoryWindow()
        {
            return Collab.instance.IsCollabEnabledForCurrentProject();
        }

        CollabHistoryPresenter m_Presenter;
        Dictionary<HistoryState, VisualElement> m_Views;
        List<CollabHistoryItem> m_HistoryItems = new List<CollabHistoryItem>();
        HistoryState m_State;
        VisualElement m_Container;
        PagedListView m_Pager;
        int m_ItemsPerPage = 5;
        string m_InProgressRev;
        bool m_RevisionActionsEnabled;

        public CollabHistoryWindow()
        {
            minSize = new Vector2(275, 50);
        }

        public void OnEnable()
        {
            SetupGUI();
            name = "CollabHistory";

            if (m_Presenter == null)
            {
                m_Presenter = new CollabHistoryPresenter(this, new CollabHistoryItemFactory(), new RevisionsService(Collab.instance, UnityConnect.instance));
            }
            m_Presenter.OnWindowEnabled();
        }

        public void OnDisable()
        {
            m_Presenter.OnWindowDisabled();
        }

        public bool revisionActionsEnabled
        {
            get { return m_RevisionActionsEnabled; }
            set
            {
                if (m_RevisionActionsEnabled == value)
                    return;

                m_RevisionActionsEnabled = value;
                foreach (var historyItem in m_HistoryItems)
                {
                    historyItem.RevisionActionsEnabled = value;
                }
            }
        }

        public void SetupGUI()
        {
            var root = this.GetRootVisualContainer();
            root.AddStyleSheetPath("StyleSheets/CollabHistoryCommon.uss");
            if (EditorGUIUtility.isProSkin)
            {
                root.AddStyleSheetPath("StyleSheets/CollabHistoryDark.uss");
            }
            else
            {
                root.AddStyleSheetPath("StyleSheets/CollabHistoryLight.uss");
            }

            m_Container = new VisualElement();
            m_Container.StretchToParentSize();
            root.Add(m_Container);

            m_Pager = new PagedListView()
            {
                name = "PagedElement",
                PagerLoc = PagerLocation.Top,
                pageSize = m_ItemsPerPage
            };

            var errorView = new StatusView()
            {
                message = "An Error Occurred",
                icon = EditorGUIUtility.LoadIconRequired("Collab.Warning") as Texture,
            };

            var noInternetView = new StatusView()
            {
                message = "No Internet Connection",
                icon = EditorGUIUtility.LoadIconRequired("Collab.NoInternet") as Texture,
            };

            var maintenanceView = new StatusView()
            {
                message = "Maintenance",
            };

            var loginView = new StatusView()
            {
                message = "Sign in to access Collaborate",
                buttonText = "Sign in...",
                callback = SignInClick,
            };

            var noSeatView = new StatusView()
            {
                message = "Ask your project owner for access to Unity Teams",
                buttonText = "Learn More",
                callback = NoSeatClick,
            };

            var waitingView = new StatusView()
            {
                message = "Connecting...",
            };

            var historyView = new ScrollView() { name = "HistoryContainer", showHorizontal = false};
            historyView.contentContainer.StretchToParentWidth();
            historyView.Add(m_Pager);

            m_Views = new Dictionary<HistoryState, VisualElement>()
            {
                {HistoryState.Error,       errorView},
                {HistoryState.Offline,     noInternetView},
                {HistoryState.Maintenance, maintenanceView},
                {HistoryState.LoggedOut,   loginView},
                {HistoryState.NoSeat,      noSeatView},
                {HistoryState.Waiting,     waitingView},
                {HistoryState.Ready,       historyView}
            };
        }

        public void UpdateState(HistoryState state, bool force)
        {
            if (state == m_State && !force)
                return;

            switch (state)
            {
                case HistoryState.Ready:
                    UpdateHistoryView(m_Pager);
                    break;
                case HistoryState.Disabled:
                    Close();
                    return;
            }

            m_State = state;
            m_Container.Clear();
            m_Container.Add(m_Views[m_State]);
        }

        public void UpdateRevisions(IEnumerable<RevisionData> datas, string tip, int totalRevisions)
        {
            var elements = new List<VisualElement>();
            var isFullDateObtained = false; // Has everything from this date been obtained?
            m_HistoryItems.Clear();

            DateTime currentDate = DateTime.MinValue;
            foreach (var data in datas)
            {
                if (data.timeStamp.Date != currentDate.Date)
                {
                    elements.Add(new CollabHistoryRevisionLine(data.timeStamp, isFullDateObtained));
                    currentDate = data.timeStamp;
                }

                var item = new CollabHistoryItem(data);
                m_HistoryItems.Add(item);

                var container = new VisualContainer();
                container.style.flexDirection = FlexDirection.Row;
                if (data.current)
                {
                    isFullDateObtained = true;
                    container.AddToClassList("currentRevision");
                    container.AddToClassList("obtainedRevision");
                }
                else if (data.obtained)
                {
                    container.AddToClassList("obtainedRevision");
                }
                else
                {
                    container.AddToClassList("absentRevision");
                }
                // If we use the index as-is, the latest commit will become #1, but we want it to be last
                container.Add(new CollabHistoryRevisionLine(data.index));
                container.Add(item);
                elements.Add(container);
            }

            m_Pager.totalItems = totalRevisions;
            m_Pager.items = elements;
        }

        public string inProgressRevision
        {
            get { return m_InProgressRev; }
            set
            {
                m_InProgressRev = value;
                foreach (var historyItem in m_HistoryItems)
                {
                    historyItem.SetInProgressStatus(value);
                }
            }
        }

        public int itemsPerPage
        {
            set
            {
                if (m_ItemsPerPage == value)
                    return;
                m_Pager.pageSize = m_ItemsPerPage;
            }
        }

        public PageChangeAction OnPageChangeAction
        {
            set { m_Pager.OnPageChange = value; }
        }

        public RevisionAction OnGoBackAction
        {
            set { CollabHistoryItem.s_OnGoBack = value; }
        }

        public RevisionAction OnUpdateAction
        {
            set { CollabHistoryItem.s_OnUpdate = value; }
        }

        public RevisionAction OnRestoreAction
        {
            set { CollabHistoryItem.s_OnRestore = value; }
        }

        public ShowBuildAction OnShowBuildAction
        {
            set { CollabHistoryItem.s_OnShowBuild = value; }
        }

        public Action OnShowServicesAction
        {
            set { CollabHistoryItem.s_OnShowServices = value; }
        }

        private void UpdateHistoryView(VisualElement history)
        {
        }

        private void NoSeatClick()
        {
            var connection = UnityConnect.instance;
            var env = connection.GetEnvironment();
            // Map environment to url - prod is special
            if (env == "production")
                env = "";
            else
                env += "-";

            var url = "https://" + env + kServiceUrl
                + "/orgs/" + connection.GetOrganizationId()
                + "/projects/" + connection.GetProjectName()
                + "/unity-teams/";
            Application.OpenURL(url);
        }

        private void SignInClick()
        {
            UnityConnect.instance.ShowLogin();
        }
    }
}
