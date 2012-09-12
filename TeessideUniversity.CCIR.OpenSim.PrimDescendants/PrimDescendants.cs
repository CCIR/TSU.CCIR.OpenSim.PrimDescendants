using System;
using System.Reflection;
using System.Collections.Generic;

using log4net;
using Nini.Config;
using Mono.Addins;
using OpenMetaverse;

using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.ScriptEngine.Shared;
using OpenSim.Region.ScriptEngine.Shared.Api;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;

[assembly: Addin("TSU.CCIR.OpenSim.PrimDescendants", "0.1")]
[assembly: AddinDependency("OpenSim", "0.7.5")]

namespace TeessideUniversity.CCIR.OpenSim
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "TSU.CCIR.OpenSim.PrimDescendants")]
    class PrimDescendants : ISharedRegionModule
    {

        #region logging

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        #endregion

        bool m_enabled = false;

        private static List<Scene> m_scenes = new List<Scene>();

        private static Dictionary<UUID, List<UUID>> m_descendants = new Dictionary<UUID, List<UUID>>();

        #region INonSharedRegionModule

        public string Name
        {
            get { return "TSU.CCIR.OpenSim.PrimDescendants"; }
        }

        public void Initialise(IConfigSource config)
        {
            IConfig conf = config.Configs["TSU.CCIR.OpenSim"];

            m_enabled = (conf != null && conf.GetBoolean("Enabled", false));

            if (m_enabled)
                m_enabled = conf.GetBoolean("PrimDescendants", false);

            m_log.Info(m_enabled ? "Enabled" : "Disabled");
        }

        public void PostInitialise() { }

        public void AddRegion(Scene scene)
        {
            m_scenes.Add(scene);
            AddEvents(scene);
        }

        public void RemoveRegion(Scene scene)
        {
            RemoveEvents(scene);
            m_scenes.Remove(scene);
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_enabled)
                return;

            IScriptModuleComms m_scriptModuleComms = scene.RequestModuleInterface<IScriptModuleComms>();

            if (m_scriptModuleComms == null)
            {
                m_log.Error("IScriptModuleComms could not be found, cannot add script functions");
                return;
            }

            m_scriptModuleComms.RegisterScriptInvocation(GetType(), new string[]{
                "primDescendantsCount",
                "primDescendantsCheck"
            });
        }

        public void Close()
        {
            m_scenes.ForEach(scene =>
            {
                RemoveEvents(scene);
            });
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        #endregion

        #region Events

        private static void AddEvents(Scene scene)
        {
            scene.EventManager.OnSceneObjectPartCopy += OnSceneObjectPartCopy;
            scene.EventManager.OnObjectBeingRemovedFromScene += OnObjectBeingRemovedFromScene;
        }

        private static void RemoveEvents(Scene scene)
        {
            scene.EventManager.OnSceneObjectPartCopy -= OnSceneObjectPartCopy;
        }

        private static void OnSceneObjectPartCopy(SceneObjectPart copy,
                SceneObjectPart original, bool userExposed)
        {
            if (userExposed && original.IsRoot)
            {
                if (!m_descendants.ContainsKey(original.UUID))
                {
                    m_descendants[original.UUID] = new List<UUID>{
                        copy.UUID
                    };
                }
                else
                {
                    m_descendants[original.UUID].Add(
                            copy.UUID);
                }
            }
        }

        private static void OnObjectBeingRemovedFromScene(SceneObjectGroup obj)
        {
            lock (m_descendants)
            {
                foreach (KeyValuePair<UUID, List<UUID>> kvp in m_descendants)
                {
                    m_descendants[kvp.Key].RemoveAll(x =>
                    {
                        return x == obj.UUID;
                    });
                }
                if (m_descendants.ContainsKey(obj.UUID))
                    m_descendants.Remove(obj.UUID);
            }
        }

        #endregion

        #region OSSL

        public static List<UUID> GetDescendants(SceneObjectGroup sog,
                int levels)
        {

            List<UUID> resp = new List<UUID>();

            if (sog != null && m_descendants.ContainsKey(sog.UUID))
            {
                levels = Util.Clip(levels, -1, levels);
                bool allLevels = levels == -1;
                if (!allLevels && levels < 0)
                    levels = 0;

                List<UUID> search = new List<UUID>{
                    sog.UUID
                };

                uint currentLevel = 0;

                while (search.Count > 0)
                {
                    ++currentLevel;
                    List<UUID> searchThese = search;
                    search = new List<UUID>();

                    foreach (UUID key in searchThese)
                    {
                        if (m_descendants.ContainsKey(key))
                        {
                            resp.AddRange(m_descendants[key]);
                            search.AddRange(m_descendants[key]);
                        }
                    }

                    if (!allLevels && currentLevel > levels)
                        break;
                }
            }

            return resp;
        }

        private static SceneObjectGroup GetSceneObjectGroupFromPartID(UUID partID)
        {
            SceneObjectPart sop = null;

            foreach (Scene scene in m_scenes)
            {
                if (scene.TryGetSceneObjectPart(partID, out sop))
                    break;
            }

            return (sop != null) ? sop.ParentGroup : null;
        }

        public static int primDescendantsCount(UUID host, UUID script)
        {
            SceneObjectGroup sog = GetSceneObjectGroupFromPartID(host);

            return (sog != null) ? GetDescendants(sog, -1).Count : 0;
        }

        public static bool CheckPrimDescendants(UUID ancestor,
                UUID possibleDescendant, bool exhaustive)
        {
            if (ancestor == possibleDescendant)
                return false;

            return !exhaustive ?
                    (m_descendants.ContainsKey(ancestor)
                    && m_descendants[ancestor].Contains(possibleDescendant)) :
                    GetDescendants(GetSceneObjectGroupFromPartID(ancestor),
                    -1).Contains(possibleDescendant);
        }

        public static int primDescendantsCheck(UUID host, UUID script,
                string objectKey)
        {
            UUID objectID = UUID.Zero;
            if (UUID.TryParse(objectKey, out objectID))
            {
                return CheckPrimDescendants(host, objectID, true) ? 1 : 0;
            }

            return 0;
        }

        #endregion
    }
}
