namespace CWTools.Games.EU4
open CWTools.Localisation
open CWTools.Validation.ValidationCore
open CWTools.Games.Files
open CWTools.Games
open CWTools.Common
open FSharp.Collections.ParallelSeq
open CWTools.Localisation.EU4Localisation
open CWTools.Utilities.Utils
open CWTools.Utilities.Position
open System.IO
open CWTools.Validation.Common.CommonValidation

type EmbeddedSettings = {
    embeddedFiles : (string * string) list
}

type ValidationSettings = {
    langs : Lang list
    validateVanilla : bool
}
type EU4Settings = {
    rootDirectory : string
    embedded : EmbeddedSettings
    validation : ValidationSettings
}


type EU4Game(settings : EU4Settings) =
    let scriptFolders = [
        "common/advisortypes";
        "common/ages";
        "common/ai_attitudes";
        "common/ai_personalities";
        "common/bookmarks";
        "common/buildings";
        "common/cb_types";
        "common/centers_of_trade";
        "common/church_aspects";
        "common/client_states";
        "common/colonial_regions";
        "common/countries";
        "common/country_colors";
        "common/country_tags";
        "common/cultures";
        "common/custom_country_colors";
        "common/custom_ideas";
        "common/decrees";
        "common/defines";
        "common/diplomatic_actions";
        "common/disasters";
        "common/dynasty_colors";
        "common/estates";
        "common/event_modifiers";
        "common/factions";
        "common/fervor";
        "common/fetishist_cults";
        "common/governments";
        "common/government_names";
        "common/government_ranks";
        "common/government_reforms";
        "common/great_projects";
        "common/ideas";
        "common/imperial_reforms";
        "common/incidents";
        "common/institutions";
        "common/insults";
        "common/isolationism";
        "common/leader_personalities";
        "common/natives";
        "common/native_advancement";
        "common/naval_doctrines";
        "common/new_diplomatic_actions";
        "common/on_actions";
        "common/opinion_modifiers";
        "common/parliament_bribes";
        "common/parliament_issues";
        "common/peace_treaties";
        "common/personal_deities";
        "common/policies";
        "common/powerprojection";
        "common/prices";
        "common/professionalism";
        "common/province_names";
        "common/province_triggered_modifiers";
        "common/rebel_types";
        "common/region_colors";
        "common/religions";
        "common/religious_conversions";
        "common/religious_reforms";
        "common/revolt_triggers";
        "common/ruler_personalities";
        "common/scripted_effects";
        "common/scripted_functions";
        "common/scripted_triggers";
        "common/state_edicts";
        "common/static_modifiers";
        "common/subject_types";
        "common/technologies";
        "common/timed_modifiers";
        "common/tradecompany_investments";
        "common/tradegoods";
        "common/tradenodes";
        "common/trade_companies";
        "common/trading_policies";
        "common/triggered_modifiers";
        "common/units";
        "common/units_display";
        "common/wargoal_types";
        "events";
        "gfx";
        "interface";]


    let fileManager = FileManager(settings.rootDirectory, None, FilesScope.All, scriptFolders, "europa universalis iv")

    let computeEU4Data (e : Entity) = EU4ComputedData()
    let resourceManager = ResourceManager(computeEU4Data)
    let resources = resourceManager.Api
    let validatableFiles() = resources.ValidatableFiles
    let mutable localisationAPIs : (bool * ILocalisationAPI) list = []
    let allLocalisation() = localisationAPIs |> List.map snd
    let validatableLocalisation() = localisationAPIs |> List.choose (fun (validate, api) -> if validate then Some api else None)
    let mutable localisationErrors : CWError list option = None

    let getEmbeddedFiles() = settings.embedded.embeddedFiles |> List.map (fun (fn, f) -> "embedded", "embeddedfiles/" + fn, f)

    let updateLocalisation() =
        localisationAPIs <-
            let locs = fileManager.LocalisationFiles() |> PSeq.ofList |> PSeq.map (fun (folder, _) -> EU4LocalisationService({ folder = folder})) |> PSeq.toList
            let allLocs = locs |> List.collect (fun l -> (settings.validation.langs)|> List.map (fun lang -> true, l.Api(lang)))
            match fileManager.ShouldUseEmbedded with
            |false -> allLocs
            |true ->
                allLocs @ (getEmbeddedFiles()
                |> List.filter (fun (_, fn, _ )-> fn.Contains("localisation"))
                |> List.map (fun (_, fn, f) -> (fn, f))
                |> (fun files -> EU4LocalisationService(files))
                |> (fun l -> (settings.validation.langs) |> List.map (fun lang -> false, l.Api(lang))))
        let taggedKeys = allLocalisation() |> List.groupBy (fun l -> l.GetLang) |> List.map (fun (k, g) -> k, g |> List.collect (fun ls -> ls.GetKeys) |> List.fold (fun (s : LocKeySet) v -> s.Add v) (LocKeySet.Empty(InsensitiveStringComparer())) )
        let validatableEntries = validatableLocalisation() |> List.groupBy (fun l -> l.GetLang) |> List.map (fun (k, g) -> k, g |> List.collect (fun ls -> ls.ValueMap |> Map.toList) |> Map.ofList)
        ()
        //lookup.proccessedLoc <- validatableEntries |> List.map (fun f -> processLocalisation lookup.scriptedEffects lookup.scriptedLoc lookup.definedScriptVariables (EntitySet (resources.AllEntities())) f taggedKeys)
        //TODO: Add processed loc bacck
    let validateAll (shallow : bool) (entities : struct (Entity * Lazy<EU4ComputedData>) list)  =
        //let ruleApplicator = RuleApplicator(lookup.configRules, lookup.typeDefs, lookup.typeDefInfo, lookup.enumDefs, loc, files, lookup.scriptedTriggers, lookup.scriptedEffects)
        eprintfn "Validating %i files" (entities.Length)
        let allEntitiesByFile = entities |> List.map (fun struct (f, _) -> f.entity)
        let flattened = allEntitiesByFile |> List.map (fun n -> n.Children) |> List.collect id
        let validators = [validateMixedBlocks, "mixed"]
        // let validators = if useRules && ruleApplicator.IsSome then (ruleApplicator.Value.RuleValidate, "rules")::validators else validators
        let oldEntities = EntitySet (resources.AllEntities())
        let newEntities = EntitySet entities
        let runValidators f (validators : (EU4StructureValidator * string) list) =
            (validators <&!!&> (fun (v, s) -> duration (fun _ -> f v) s) |> (function |Invalid es -> es |_ -> []))
        eprintfn "Validating misc"
        //let res = validators |> List.map (fun v -> v oldEntities newEntities) |> List.fold (<&&>) OK
        let res = runValidators (fun f -> f oldEntities newEntities) validators
        //let res = validators <&!&> (fun v -> v oldEntities newEntities) |> (function |Invalid es -> es |_ -> [])
        eprintfn "Validating files"
        // let fileValidators = [valSpriteFiles, "sprites"; valMeshFiles, "mesh"; valAssetFiles, "asset"; valComponentIcons, "compicon"]
        // let fres = fileValidators <&!&> (fun (v, s) -> duration (fun _ -> v resources newEntities) s) |> (function |Invalid es -> es |_ -> [])
        eprintfn "Validating effects/triggers"
        // let eres = duration (fun _ -> valAllEffects (lookup.scriptedTriggers) (lookup.scriptedEffects) (lookup.staticModifiers) newEntities  |> (function |Invalid es -> es |_ -> [])) "effects"
        // let tres = duration (fun _ ->  valAllTriggers (lookup.scriptedTriggers) (lookup.scriptedEffects) (lookup.staticModifiers) newEntities  |> (function |Invalid es -> es |_ -> [])) "triggers"
        // let wres = duration (fun _ ->  validateModifierBlocks (lookup.scriptedTriggers) (lookup.scriptedEffects) (lookup.staticModifiers) newEntities |> (function |Invalid es -> es |_ -> [])) "weights"
        // let mres = duration (fun _ ->  valAllModifiers (lookup.coreModifiers) newEntities  |> (function |Invalid es -> es |_ -> [])) "modifiers"
        // let evres = duration (fun _ ->  ( if settings.validation.experimental && (not(shallow)) then getEventChains (lookup.scriptedEffects) oldEntities newEntities else OK) |> (function |Invalid es -> es |_ -> [])) "events"
        //let etres = getEventChains newEntities |> (function |Invalid es -> es |_ -> [])
        //(validateShips (flattened)) @ (validateEvents (flattened)) @ res @ fres @ eres
        // let shallow = (validateShips (flattened)) @ (validateEvents (flattened)) @ res @ fres @ eres @ tres @ mres @ wres
        // let deep = evres
        let shallow = res
        let deep = []
        shallow, deep
    let localisationCheck (entities : struct (Entity * Lazy<EU4ComputedData>) list) =
        eprintfn "Localisation check %i files" (entities.Length)
        //let keys = allLocalisation() |> List.groupBy (fun l -> l.GetLang) |> List.map (fun (k, g) -> k, g |>List.collect (fun ls -> ls.GetKeys) |> Set.ofList )
        //let allEntries = allLocalisation() |> List.groupBy (fun l -> l.GetLang) |> List.map (fun (k, g) -> k, g |> List.collect (fun ls -> ls.ValueMap |> Map.toList) |> Map.ofList)
        let validators = []
        // let validators = [valEventLocs; valTechLocs; valCompSetLocs; valCompTempLocs; valBuildingLocs; valTraditionLocCats; valArmiesLoc;
        //                      valArmyAttachmentLocs; valDiploPhrases; valShipLoc; valFactionDemands; valSpeciesRightsLocs;
        //                      valMapsLocs; valMegastructureLocs; valModifiers; valModules; valTraits; valGoverments; valPersonalities;
        //                      valEthics; valPlanetClasses; valEdicts; valPolicies; valSectionTemplates; valSpeciesNames; valStratRes;
        //                      valAmbient; valDeposits; valWarGoals; valEffectLocs; valTriggerLocs; valBuildingTags; valOpinionModifiers;
        //                      valScriptedTriggers; valSpecialProjects; valStarbaseType; valTileBlockers; valAnomalies]
        let newEntities = EntitySet entities
        let oldEntities = EntitySet (resources.AllEntities())
        // let vs = (validators |> List.map (fun v -> v oldEntities localisationKeys newEntities) |> List.fold (<&&>) OK)
        // ((vs) |> (function |Invalid es -> es |_ -> []))
        []

    let parseErrors() =
        resources.GetResources()
            |> List.choose (function |EntityResource (_, e) -> Some e |_ -> None)
            |> List.choose (fun r -> r.result |> function |(Fail (result)) when r.validate -> Some (r.filepath, result.error, result.position)  |_ -> None)
    let makeEntityResourceInput filepath filetext  =
        let filepath = Path.GetFullPath(filepath)
        let indexOfScope = filepath.IndexOf(fileManager.ScopeDirectory)
        let rootedpath =
            if indexOfScope = -1
            then filepath
            else filepath.Substring(indexOfScope + (fileManager.ScopeDirectory.Length))
        let logicalpath = fileManager.ConvertPathToLogicalPath rootedpath
        EntityResourceInput {scope = ""; filepath = filepath; logicalpath = logicalpath; filetext = filetext; validate = true}
    let mutable errorCache = Map.empty

    let updateFile (shallow : bool) filepath (filetext : string option) =
        eprintfn "%s" filepath
        let timer = new System.Diagnostics.Stopwatch()
        timer.Start()
        let res =
            match filepath with
            |x when x.EndsWith (".yml") ->
                updateLocalisation()
                // let les = (localisationCheck (resources.ValidatableEntities())) @ globalLocalisation()
                // localisationErrors <- Some les
                // globalLocalisation()
                []
            | _ ->
                let filepath = Path.GetFullPath(filepath)
                let file = filetext |> Option.defaultWith (fun () -> File.ReadAllText filepath)
                let rootedpath = filepath.Substring(filepath.IndexOf(fileManager.ScopeDirectory) + (fileManager.ScopeDirectory.Length))
                let logicalpath = fileManager.ConvertPathToLogicalPath rootedpath
                let resource = makeEntityResourceInput filepath file

                //eprintfn "%s %s" logicalpath filepath
                let newEntities = resources.UpdateFile (resource) |> List.map snd
                // match filepath with
                // |x when x.Contains("scripted_triggers") -> updateScriptedTriggers()
                // |x when x.Contains("scripted_effects") -> updateScriptedEffects()
                // |x when x.Contains("static_modifiers") -> updateStaticodifiers()
                // |_ -> ()
                // updateDefinedVariables()
                // validateAll true newEntities @ localisationCheck newEntities
                match shallow with
                    |true ->
                        let (shallowres, _) = (validateAll shallow newEntities)
                        let shallowres = shallowres @ (localisationCheck newEntities)
                        let deep = errorCache |> Map.tryFind filepath |> Option.defaultValue []
                        shallowres @ deep
                    |false ->
                        let (shallowres, deepres) = (validateAll shallow newEntities)
                        let shallowres = shallowres @ (localisationCheck newEntities)
                        errorCache <- errorCache.Add(filepath, deepres)
                        shallowres @ deepres


        eprintfn "Update Time: %i" timer.ElapsedMilliseconds
        res


    do
        eprintfn "Parsing %i files" (fileManager.AllFilesByPath().Length)
        let files = fileManager.AllFilesByPath()
        let filteredfiles = if settings.validation.validateVanilla then files else files |> List.choose (function |FileResourceInput f -> Some (FileResourceInput f) |EntityResourceInput f -> if f.scope = "vanilla" then Some (EntityResourceInput {f with validate = false}) else Some (EntityResourceInput f))
        resources.UpdateFiles(filteredfiles) |> ignore
        let embedded = settings.embedded.embeddedFiles |> List.map (fun (f, ft) -> if ft = "" then FileResourceInput { scope = "embedded"; filepath = f; logicalpath = (fileManager.ConvertPathToLogicalPath f) } else EntityResourceInput {scope = "embedded"; filepath = f; logicalpath = (fileManager.ConvertPathToLogicalPath f); filetext = ft; validate = false})
        if fileManager.ShouldUseEmbedded then resources.UpdateFiles(embedded) |> ignore else ()

        // updateScriptedTriggers()
        // updateScriptedEffects()
        // updateStaticodifiers()
        // updateScriptedLoc()
        // updateDefinedVariables()
        // updateModifiers()
        // updateTechnologies()
        updateLocalisation()
        // updateTypeDef()
    interface IGame<EU4ComputedData> with
    //member __.Results = parseResults
        member __.ParserErrors() = parseErrors()
        member __.ValidationErrors() = let (s, d) = (validateAll false (resources.ValidatableEntities())) in s @ d
        member __.LocalisationErrors(force : bool) = []
            // let generate =
            //     let les = (localisationCheck (resources.ValidatableEntities())) @ globalLocalisation()
            //     localisationErrors <- Some les
            //     les
            // match localisationErrors with
            // |Some les -> if force then generate else les
            // |None -> generate

        //member __.ValidationWarnings = warningsAll
        member __.Folders() = fileManager.AllFolders()
        member __.AllFiles() =
            resources.GetResources()
            // |> List.map
            //     (function
            //         |EntityResource (f, r) ->  r.result |> function |(Fail (result)) -> (r.filepath, false, result.parseTime) |Pass(result) -> (r.filepath, true, result.parseTime)
            //         |FileResource (f, r) ->  (r.filepath, false, 0L))
            //|> List.map (fun r -> r.result |> function |(Fail (result)) -> (r.filepath, false, result.parseTime) |Pass(result) -> (r.filepath, true, result.parseTime))
        member __.ScriptedTriggers() = []//lookup.scriptedTriggers
        member __.ScriptedEffects() = [] //lookup.scriptedEffects
        member __.StaticModifiers() = [] //lookup.staticModifiers
        member __.UpdateFile shallow file text = updateFile shallow file text
        member __.AllEntities() = resources.AllEntities()
        member __.References() = References<EU4ComputedData>(resources, Lookup(), (localisationAPIs |> List.map snd))
        member __.Complete pos file text = [] //completion pos file text
        member __.ScopesAtPos pos file text = None //scopesAtPos pos file text
        member __.GoToType pos file text = Some range0
        member __.FindAllRefs pos file text = Some [range0]
        member __.ReplaceConfigRules rules = ()
        member __.RefreshCaches() = ()
        member __.ForceRecompute() = ()

            //member __.ScriptedTriggers = parseResults |> List.choose (function |Pass(f, p, t) when f.Contains("scripted_triggers") -> Some p |_ -> None) |> List.map (fun t -> )