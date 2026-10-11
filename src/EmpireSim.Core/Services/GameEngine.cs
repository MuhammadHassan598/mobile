using EmpireSim.Core.Models;

namespace EmpireSim.Core.Services;

/// <summary>
/// The single entry point the UI talks to. Owns the game state, the clock
/// and the simulation; raises <see cref="StateChanged"/> after every tick
/// so Blazor components can re-render (via InvokeAsync on the UI thread).
/// Registered as a singleton in MauiProgram.
/// </summary>
public sealed partial class GameEngine : IDisposable
{
    private readonly SimulationService _sim;
    private readonly SaveService _save;
    private bool _disposed;

    public GameClock Clock { get; } = new();
    public GameState State { get; private set; }

    /// <summary>Raised after every simulated day and after load/new-game.</summary>
    public event Action? StateChanged;

    public GameEngine(SimulationService sim, SaveService save)
    {
        _sim = sim;
        _save = save;
        State = GameState.NewGame();
        CampaignChosen = false;
        Clock.DayElapsed += OnDayElapsed;
    }

    /// <summary>False until the player picks a nation on the start screen.</summary>
    public bool CampaignChosen { get; private set; }

    /// <summary>Starts a campaign as the chosen nation.</summary>
    public void StartCampaign(string nationId)
    {
        Clock.SetSpeed(GameSpeed.Paused);
        State = GameState.NewGame(nationId);
        CampaignChosen = true;
        StateChanged?.Invoke();
    }

    /// <summary>Returns to the nation-select screen.</summary>
    public void AbandonToMenu()
    {
        Clock.SetSpeed(GameSpeed.Paused);
        CampaignChosen = false;
        StateChanged?.Invoke();
    }

    public void NewGame()
    {
        StartCampaign("ottoman");
    }

    public async Task SaveAsync()
    {
        await _save.SaveAsync(State);
        State.Log("Game saved.");
        StateChanged?.Invoke();
    }

    public async Task LoadAsync()
    {
        var loaded = await _save.LoadAsync();
        if (loaded is not null)
        {
            Clock.SetSpeed(GameSpeed.Paused);
            State = loaded;
            TreatyService.MigrateLegacy(State);
            CampaignChosen = true;
            State.Log("Save loaded.");
            StateChanged?.Invoke();
        }
    }

    public bool HasSave => _save.HasSave;

    /// <summary>
    /// Daily income shown in the top bar and nation-select screen: the same tax revenue
    /// the daily tick pays (workforce x tax rates x multipliers) plus crown domain income.
    /// </summary>
    public static long EstimateDailyIncome(Nation n)
    {
        n.EnsureTaxationInitialized();
        double taxMult = n.HasCommander(CommanderRole.CommanderInChief) ? Balance.CinCTaxMult : 1.0;
        double tax = TaxationService.TotalRevenue(n.Workforce, n.TaxRates) * taxMult * n.TaxMult;
        return (long)(tax + Balance.CrownDomainIncomePerDay);
    }

    /// <summary>Whether the player can afford a building right now.</summary>
    public bool CanAfford(BuildingSpec spec, double costMult = 1.0)
    {
        var n = State.PlayerNation;
        return n.Gold >= spec.GoldCost * costMult
            && n.Wood >= spec.WoodCost * costMult
            && n.Iron >= spec.IronCost * costMult;
    }

    /// <summary>
    /// Starts construction of a building for the whole nation.
    /// Costs are paid upfront. Returns an error message, or null on success.
    /// </summary>
    public string? StartConstruction(BuildingType type)
    {
        var nation = State.PlayerNation;

        var spec = BuildingCatalog.Get(type);

        if (nation.ConstructionQueue.Count >= Balance.MaxBuildQueue)
            return $"Build queue is full (max {Balance.MaxBuildQueue}).";

        double costMult = LawService.ConstructionCostMult(nation);
        if (!CanAfford(spec, costMult)) return "Not enough resources.";

        nation.PayGold(spec.GoldCost * costMult);
        nation.Wood -= spec.WoodCost * costMult;
        nation.Iron -= spec.IronCost * costMult;
        int buildDays = Math.Max(1, (int)Math.Ceiling(spec.BuildDays * ReligionService.ConstructionTimeMult(nation) * LawService.ConstructionTimeMult(nation)));
        nation.ConstructionQueue.Add(new ConstructionProject
        {
            Building = type,
            DaysLeft = buildDays,
            TotalDays = buildDays,
        });
        State.Log($"Started building {spec.Name} ({buildDays:N0} days).");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Starts construction of a production building.</summary>
    public string? BuildProduction(string buildingId, int count = 1)
    {
        var nation = State.PlayerNation;
        var spec = ProductionCatalog.Get(buildingId);

        if (count <= 0) return "Invalid count.";
        if (nation.ConstructionQueue.Count + count > Balance.MaxBuildQueue)
            return $"Build queue is full (max {Balance.MaxBuildQueue}).";

        double totalGold = spec.GoldCost * count;
        double totalWood = spec.WoodCost * count;
        double totalStone = spec.StoneCost * count;
        double totalIron = spec.IronCost * count;

        if (!nation.CanPay(totalGold) || nation.Wood < totalWood || nation.Stone < totalStone || nation.Iron < totalIron)
            return "Not enough resources.";

        nation.PayGold(totalGold);
        nation.Wood -= totalWood;
        nation.Stone -= totalStone;
        nation.Iron -= totalIron;
        for (int i = 0; i < count; i++)
        {
            nation.ConstructionQueue.Add(new ConstructionProject
            {
                Building = BuildingType.Farm, // placeholder, ProductionBuildingId is used
                ProductionBuildingId = buildingId,
                DaysLeft = spec.BuildDays,
                TotalDays = spec.BuildDays,
            });
        }
        State.Log($"Started building {count}x {spec.Name} ({spec.BuildDays} days each).");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Queue timed recruitment with equipment. Returns error or null.</summary>
    public string? StartRecruitment(UnitType type, int count)
    {
        if (count <= 0) return "Invalid count.";
        var spec = UnitCatalog.Get(type);
        var n = State.PlayerNation;

        if (n.RecruitmentQueue.Count >= Balance.MaxRecruitmentQueue)
            return "Recruitment capacity reached.";

        double mult = n.HasCommander(CommanderRole.LandCommander) ? Balance.LandCommanderRecruitMult : 1.0;
        double gold = spec.GoldCost * mult * count;
        double wood = spec.WoodCost * count;
        double iron = spec.IronCost * count;

        // Check equipment
        var equipNeeded = new Dictionary<string, double>();
        if (spec.Equipment is not null)
        {
            foreach (var eq in spec.Equipment)
            {
                double need = eq.PerSoldier * count;
                if (n.GetMilitaryItem(eq.ItemId) < need)
                    return $"Not enough {eq.ItemId} (need {need:N0}).";
                equipNeeded[eq.ItemId] = need;
            }
        }

        if (!n.CanPay(gold) || n.Wood < wood || n.Iron < iron)
            return "Not enough resources.";

        // Deduct upfront
        n.PayGold(gold);
        n.Wood -= wood;
        n.Iron -= iron;
        foreach (var kv in equipNeeded)
            n.AddMilitaryItem(kv.Key, -kv.Value);

        double days = Balance.RecruitDaysPerSoldier * count * LawService.RecruitmentTimeMult(n);
        // Assembly: military restriction doubles recruitment time
        if (AssemblyService.HasActivePolicy(State.ActiveAssemblyPolicies, "military_restriction", n.Id, State.CurrentDate))
            days *= 2.0;
        days = Math.Max(1, Math.Min(days, Balance.MaxRecruitDays));
        n.RecruitmentQueue.Add(new RecruitmentProject
        {
            Type = type, Count = count, TotalDays = days, DaysLeft = days,
            EquipmentUsed = equipNeeded, GoldPaid = gold
        });

        State.Log($"Recruiting {count:N0} {spec.Name} ({days:N0} days).");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Cancel a recruitment project with 50% gold refund. Equipment not refunded.</summary>
    public string? CancelRecruitment(string projectId)
    {
        var n = State.PlayerNation;
        var proj = n.RecruitmentQueue.FirstOrDefault(p => p.Id == projectId);
        if (proj is null) return "Not found.";
        n.Gold += proj.GoldPaid * 0.5;
        n.RecruitmentQueue.Remove(proj);
        State.Log($"Cancelled recruitment of {proj.Count:N0} {UnitCatalog.Get(proj.Type).Name} (50% refund).");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Emergency conscription. Returns error or null.</summary>
    public string? Conscript(string levelId)
    {
        var level = ConscriptionLevels.Get(levelId);
        if (level is null) return "Invalid level.";
        var n = State.PlayerNation;

        int count = (int)(n.Population * level.PopulationFraction);
        if (count <= 0) return "No eligible population.";
        double gold = level.GoldCostPerSoldier * count;
        if (!n.CanPay(gold)) return "Not enough gold.";

        n.PayGold(gold);
        // Add as pikemen (militia) directly with unrest penalty
        var stack = n.Units.FirstOrDefault(u => u.Type == UnitType.Pikeman);
        if (stack is null) n.Units.Add(new UnitStack { Type = UnitType.Pikeman, Count = count });
        else stack.Count += count;

        n.Unrest += level.UnrestPenalty;
        State.Log($"{level.Name}: raised {count:N0} militia. Unrest +{level.UnrestPenalty}.");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Hire mercenaries. Returns error or null.</summary>
    public string? HireMercenaries(string companyId, int count)
    {
        var company = MercenaryCatalog.Get(companyId);
        if (company is null || count <= 0) return "Invalid.";
        var n = State.PlayerNation;

        if (count > company.Available) return "Not enough available.";
        double cost = company.HireCostPerSoldier * count;
        if (!n.CanPay(cost)) return "Not enough gold.";

        n.PayGold(cost);
        n.MercenaryContracts.Add(new MercenaryContract
        {
            CompanyId = companyId, Count = count,
            Expires = State.CurrentDate.AddDays(company.ContractDays)
        });
        // Add as active units
        var stack = n.Units.FirstOrDefault(u => u.Type == company.UnitType);
        if (stack is null) n.Units.Add(new UnitStack { Type = company.UnitType, Count = count });
        else stack.Count += count;

        State.Log($"Hired {count:N0} {company.Name} for {Currency.Cost(cost)}.");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Mobilize reserves into active duty. Returns error or null.</summary>
    public string? MobilizeReserves(int count)
    {
        var n = State.PlayerNation;
        if (count <= 0 || count > n.Reserves) return "Not enough reserves.";
        double gold = Balance.MobilizeCostPerSoldier * count;
        if (!n.CanPay(gold)) return "Not enough gold.";

        n.PayGold(gold);
        n.Reserves -= count;
        var stack = n.Units.FirstOrDefault(u => u.Type == UnitType.Musketeer);
        if (stack is null) n.Units.Add(new UnitStack { Type = UnitType.Musketeer, Count = count });
        else stack.Count += count;

        State.Log($"Mobilized {count:N0} reserves.");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>
    /// Request allied military assistance (relations 70+). The troops are TAKEN FROM THE ALLY'S ARMY, unit type by unit type,
    /// and join the player's: they are moved, never copied. A request costs a little goodwill and has a cooldown.
    /// Returns the result message.
    /// </summary>
    public string RequestAlliedAssistance(string nationId)
    {
        var n = State.PlayerNation;
        var ally = State.OtherNations.FirstOrDefault(x => x.Id == nationId && !x.IsEliminated);
        if (ally is null) return "Nation not found.";
        int rating = DiplomacyService.ToDisplayRating(ally.RelationToPlayer);
        if (rating < Balance.AlliedHelpMinRating) return $"{ally.Name} refuses (relations too low).";
        if (CooldownMessage("alliedhelp", ally) is { } wait) return $"{ally.Name}: {wait}";

        int contingent = (int)(ally.Soldiers * Balance.AlliedHelpFraction);
        if (contingent <= 0) return $"{ally.Name} has no troops to spare.";

        var force = ArmyHelper.ExtractSoldiers(ally, contingent);
        int sent = force.Sum(s => s.Count);
        if (sent <= 0) return $"{ally.Name} has no troops to spare.";
        ArmyHelper.MergeStacks(n, force);
        DiplomacyService.AddRating(ally, -Balance.AlliedHelpRatingCost);
        StartCooldown("alliedhelp", ally, Balance.AlliedHelpCooldownDays);

        State.LogMovement(MovementKind.Troops, MovementStatus.Completed, ally, n, $"{ally.Name} sent {sent:N0} allied troops.", inbox: InboxTopic.AllyAssistance);
        StateChanged?.Invoke();
        return $"{ally.Name} sends {sent:N0} troops!";
    }

    /// <summary>Buy product from another country. Gold paid now, goods delivered later.</summary>
    public string? BuyProduct(string sellerId, string productId, double quantity)
    {
        if (quantity <= 0) return "Invalid quantity.";
        var buyer = State.PlayerNation;
        var seller = State.AllNations().FirstOrDefault(n => n.Id == sellerId);
        if (seller is null || seller.Id == buyer.Id) return "Invalid seller.";
        var product = TradeCatalog.Get(productId);
        if (product is null || !product.CanBuy) return "Cannot buy this product.";
        // Assembly: weapon embargo blocks equipment purchases from/to target
        if (product.Category == "Equipment")
        {
            if (AssemblyService.HasActivePolicy(State.ActiveAssemblyPolicies, "weapon_sales_ban", sellerId, State.CurrentDate) ||
                AssemblyService.HasActivePolicy(State.ActiveAssemblyPolicies, "weapon_embargo", sellerId, State.CurrentDate))
                return "Weapon sales to this country are prohibited by Assembly.";
        }

        // Check seller stock (they won't sell more than 50%)
        double sellerStock = seller.GetProductStock(productId) * 0.5;
        if (sellerStock < quantity) return $"Seller only has {sellerStock:N0} available.";

        double pricePer1000 = BuyPricePer1000(sellerId, productId);
        double total = MarketPricing.TotalValue(pricePer1000, quantity);
        if (!buyer.CanPay(total)) return "Not enough gold.";

        // Deduct gold now, reserve seller stock
        buyer.PayGold(total);
        seller.AdjustProductStock(productId, -quantity);

        int days = MarketPricing.DeliveryDays(buyer.Id, sellerId);
        var contract = new TradeContract
        {
            BuyerId = buyer.Id, SellerId = sellerId, ProductId = productId,
            Quantity = quantity, PricePer1000 = pricePer1000, TotalValue = total,
            CreatedDate = State.CurrentDate,
            DeliveryDate = State.CurrentDate.AddDays(days),
            Status = TradeStatus.InTransit, IsPlayerBuyer = true
        };
        State.TradeContracts.Add(contract);
        State.LogMovement(MovementKind.Goods, MovementStatus.UnderWay, seller, buyer, $"Bought {quantity:N0} {product.Name} from {seller.Name} for {Currency.Cost(total)}. Delivery in {days}d.");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>
    /// What the player actually pays per 1,000 units of a product from a seller: the market price with the import-law discount
    /// and the trade-agreement discount applied (the very price <see cref="BuyProduct"/> charges).
    /// </summary>
    public double BuyPricePer1000(string sellerId, string productId)
    {
        var buyer = State.PlayerNation;
        double pricePer1000 = MarketPricing.PricePer1000(productId, sellerId);
        // Law: import price discount
        pricePer1000 *= LawService.ImportPriceMult(buyer);
        // Trade agreement: partners sell to us cheaper.
        if (TreatyService.Has(State, TreatyType.TradeAgreement, buyer.Id, sellerId))
            pricePer1000 *= Balance.TradeAgreementImportMult;
        return pricePer1000;
    }

    /// <summary>
    /// The most the player can buy of a product from a seller right now, in whole units: limited by what the seller will part with
    /// (half its stock) and by the player's gold. Zero when nothing can be bought.
    /// </summary>
    public double MaxBuyQuantity(string sellerId, string productId)
    {
        var buyer = State.PlayerNation;
        var seller = State.AllNations().FirstOrDefault(n => n.Id == sellerId);
        if (seller is null || seller.Id == buyer.Id) return 0;
        double price = BuyPricePer1000(sellerId, productId);
        if (price <= 0) return 0;

        double max = Math.Floor(Math.Min(seller.GetProductStock(productId) * 0.5, buyer.Gold * 1000 / price));
        while (max > 0 && !buyer.CanPay(MarketPricing.TotalValue(price, max))) max--;   // (the total is rounded to the cent)
        return Math.Max(0, max);
    }

    /// <summary>
    /// The most the player can sell of a product to a buyer at a price per 1,000, in whole units: limited by the player's stock
    /// and by what the buyer can pay. Zero when the player has none.
    /// </summary>
    public double MaxSellQuantity(string buyerId, string productId, double pricePer1000)
    {
        var seller = State.PlayerNation;
        var buyer = State.AllNations().FirstOrDefault(n => n.Id == buyerId);
        if (buyer is null || buyer.Id == seller.Id) return 0;

        double max = Math.Floor(seller.GetProductStock(productId));
        if (pricePer1000 > 0)
        {
            max = Math.Min(max, Math.Floor(buyer.Gold * 1000 / pricePer1000));
            while (max > 0 && buyer.Gold < MarketPricing.TotalValue(pricePer1000, max)) max--;
        }
        return Math.Max(0, max);
    }

    /// <summary>Sell product to another country. Goods deducted now, gold paid on delivery.</summary>
    public string? SellProduct(string buyerId, string productId, double quantity, double pricePer1000)
    {
        if (quantity <= 0) return "Invalid quantity.";
        if (pricePer1000 <= 0) return "Invalid price.";
        var seller = State.PlayerNation;
        var buyer = State.AllNations().FirstOrDefault(n => n.Id == buyerId);
        if (buyer is null || buyer.Id == seller.Id) return "Invalid buyer.";
        var product = TradeCatalog.Get(productId);
        if (product is null || !product.CanSell) return "Cannot sell this product.";

        double available = seller.GetProductStock(productId);
        if (available < quantity) return $"Only {available:N0} available.";

        double total = MarketPricing.TotalValue(pricePer1000, quantity);
        // Buyer must be able to pay (check their gold)
        if (buyer.Gold < total) return $"{buyer.Name} cannot afford this.";

        // Deduct goods now
        seller.AdjustProductStock(productId, -quantity);

        int days = MarketPricing.DeliveryDays(seller.Id, buyerId);
        var contract = new TradeContract
        {
            BuyerId = buyerId, SellerId = seller.Id, ProductId = productId,
            Quantity = quantity, PricePer1000 = pricePer1000, TotalValue = total,
            CreatedDate = State.CurrentDate,
            DeliveryDate = State.CurrentDate.AddDays(days),
            Status = TradeStatus.InTransit, IsPlayerBuyer = false
        };
        State.TradeContracts.Add(contract);
        State.LogMovement(MovementKind.Goods, MovementStatus.UnderWay, seller, buyer, $"Sold {quantity:N0} {product.Name} to {buyer.Name} for {Currency.Cost(total)}. Delivery in {days}d.");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Start religion conversion. Returns error or null.</summary>
    public string? StartReligionConversion(string religionId)
    {
        var nation = State.PlayerNation;
        var target = ReligionCatalog.Get(religionId);
        if (target is null) return "Invalid religion.";
        if (nation.ReligionConversion?.InProgress == true)
            return "A conversion is already in progress.";
        if (nation.Religion.Equals(target.Name, StringComparison.OrdinalIgnoreCase))
            return "This is already the official religion.";
        if (!nation.CanPay(target.ConversionCost))
            return "Insufficient Gold.";

        nation.PayGold(target.ConversionCost);
        nation.ReligionConversion = new ReligionConversion
        {
            TargetReligionId = religionId,
            StartDate = State.CurrentDate,
            ExpectedCompletion = State.CurrentDate.AddDays(target.ConversionDays),
            PaidCost = target.ConversionCost
        };
        State.Log($"Started conversion to {target.Name} ({target.ConversionDays} days).");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Select a national law. Replaces conflicting law in same policy group. Returns error or null.</summary>
    public string? SelectLaw(string lawId)
    {
        var nation = State.PlayerNation;
        var law = LawCatalog.Get(lawId);
        if (law is null) return "Invalid law.";
        if (nation.ActiveLaws.Contains(lawId)) return "Law already active.";
        if (!nation.CanPay(law.SelectionCost)) return "Insufficient Gold.";

        // Remove conflicting law in same policy group (except 'general' and 'diplomatic' which coexist)
        if (law.PolicyGroup != "general" && law.PolicyGroup != "diplomatic")
        {
            var conflict = nation.ActiveLaws
                .Select(id => LawCatalog.Get(id))
                .FirstOrDefault(l => l is not null && l.PolicyGroup == law.PolicyGroup);
            if (conflict is not null)
            {
                nation.ActiveLaws.Remove(conflict.Id);
                State.Log($"Replaced {conflict.Name} with {law.Name}.");
            }
        }

        nation.PayGold(law.SelectionCost);
        nation.ActiveLaws.Add(lawId);
        State.Log($"Enacted law: {law.Name} ({Currency.Cost(law.SelectionCost)}).");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Submit an assembly proposal. Returns error or null.</summary>
    public string? SubmitProposal(string typeId, string targetId, int effectDays, int votingDays = 30)
    {
        var proposer = State.PlayerNation;
        var type = AssemblyProposalTypes.Get(typeId);
        if (type is null) return "Invalid proposal type.";
        var target = State.AllNations().FirstOrDefault(n => n.Id == targetId);
        if (target is null || target.Id == proposer.Id) return "Invalid target.";

        var proposal = new AssemblyProposal
        {
            TypeId = typeId,
            ProposerId = proposer.Id,
            TargetId = targetId,
            EffectDurationDays = effectDays,
            CreatedDate = State.CurrentDate,
            VotingDeadline = State.CurrentDate.AddDays(votingDays),
            Status = ProposalStatus.VotingOpen
        };
        // Proposer auto-votes FOR
        proposal.Votes[proposer.Id] = true;
        State.AssemblyProposals.Add(proposal);
        State.LogMovement(MovementKind.Assembly, MovementStatus.UnderWay, proposer, target,
            $"Proposal submitted: {type.Name} against {target.Name}. Voting closes in {votingDays}d.");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Recruit personnel (military/spies/saboteurs). Transfers from civilian workforce.</summary>
    public string? RecruitPersonnel(string personnelId, long quantity)
    {
        var def = PersonnelCatalog.Get(personnelId);
        if (def is null || quantity <= 0) return "Invalid.";
        var nation = State.PlayerNation;
        nation.EnsureTaxationInitialized();

        double totalCost = def.GoldCostPerPerson * quantity;
        if (!nation.CanPay(totalCost))
            return $"Insufficient Gold (need {totalCost:N0}).";

        // Find eligible source with enough people
        string? source = null;
        foreach (var s in def.EligibleSources)
        {
            if (PopulationService.GetGroupCount(nation.Workforce, s) >= quantity)
            {
                source = s;
                break;
            }
        }
        if (source is null)
            return "Not enough eligible civilian workers.";

        nation.PayGold(totalCost);
        var err = PopulationService.Transfer(nation.Workforce, source, personnelId, quantity);
        if (err is not null) return err;

        // Military personnel join reserves
        if (personnelId == "military")
        {
            nation.Reserves += (int)quantity;
            // Sync workforce military with soldiers + reserves
            nation.Workforce.MilitaryPersonnel = nation.Soldiers + nation.Reserves;
        }

        State.Log($"Recruited {quantity:N0} {def.Name} from {source} ({Currency.Cost(totalCost)}).");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Start a national event. Returns error or null.</summary>
    public string? StartNationalEvent(string eventId)
    {
        var nation = State.PlayerNation;
        var def = NationalEventCatalog.Get(eventId);
        if (def is null) return "Invalid event.";

        // Check if same event already in progress
        if (nation.NationalEvents.Any(e => e.EventTypeId == eventId && e.Status == NationalEventStatus.InProgress))
            return "This event is already in progress.";

        if (!nation.CanPay(def.GoldCost))
            return $"Insufficient Gold (need {def.GoldCost:N0}).";
        if (def.FoodRequired > 0 && nation.GetProduct("Wheat") < def.FoodRequired)
            return $"Not enough Wheat (need {def.FoodRequired:N0}).";

        nation.PayGold(def.GoldCost);
        if (def.FoodRequired > 0)
            nation.AddProduct("Wheat", -def.FoodRequired);

        nation.NationalEvents.Add(new NationalEventInstance
        {
            EventTypeId = eventId,
            StartDate = State.CurrentDate,
            ExpectedCompletion = State.CurrentDate.AddDays(def.DurationDays),
            Status = NationalEventStatus.InProgress,
            PaidCost = def.GoldCost
        });
        State.Log($"Started {def.Name} ({def.DurationDays} days).");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Cast a vote on a proposal.</summary>
    public string? CastVote(string proposalId, bool forProposal)
    {
        var nation = State.PlayerNation;
        var proposal = State.AssemblyProposals.FirstOrDefault(p => p.Id == proposalId);
        if (proposal is null) return "Not found.";
        if (proposal.Status != ProposalStatus.VotingOpen) return "Voting closed.";
        if (State.CurrentDate > proposal.VotingDeadline) return "Deadline passed.";
        if (proposal.Votes.ContainsKey(nation.Id)) return "Already voted.";

        proposal.Votes[nation.Id] = forProposal;
        State.Log($"{nation.Name} voted {(forProposal ? "FOR" : "AGAINST")} proposal.");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Repeal an active law.</summary>
    public void RepealLaw(string lawId)
    {
        var nation = State.PlayerNation;
        if (nation.ActiveLaws.Remove(lawId))
        {
            var law = LawCatalog.Get(lawId);
            State.Log($"Repealed law: {law?.Name ?? lawId}.");
            StateChanged?.Invoke();
        }
    }

    /// <summary>Cancel active religion conversion (no refund).</summary>
    public void CancelReligionConversion()
    {
        var nation = State.PlayerNation;
        if (nation.ReligionConversion?.InProgress == true)
        {
            State.Log($"Cancelled conversion to {nation.ReligionConversion.TargetReligionId}.");
            nation.ReligionConversion = null;
            StateChanged?.Invoke();
        }
    }

    /// <summary>Starts crafting a batch (10 units) of a military item.</summary>
    public string? StartMilitaryCraft(string recipeId)
    {
        var nation = State.PlayerNation;
        var recipe = MilitaryRecipes.Get(recipeId);

        if (nation.Wood < recipe.WoodCost || nation.Stone < recipe.StoneCost ||
            nation.Iron < recipe.IronCost || nation.Copper < recipe.CopperCost ||
            nation.Lead < recipe.LeadCost)
            return "Not enough resources.";

        nation.Wood -= recipe.WoodCost;
        nation.Stone -= recipe.StoneCost;
        nation.Iron -= recipe.IronCost;
        nation.Copper -= recipe.CopperCost;
        nation.Lead -= recipe.LeadCost;

        nation.MilitaryCraftQueue.Add(new MilitaryCraftProject
        {
            RecipeId = recipeId,
            DaysLeft = recipe.Days,
            TotalDays = recipe.Days,
        });
        State.Log($"Started crafting 10x {recipe.Name} ({recipe.Days} days).");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Cancels a military craft project (refunds 50% of resources).</summary>
    public void CancelMilitaryCraft(string projectId)
    {
        var nation = State.PlayerNation;
        var proj = nation.MilitaryCraftQueue.FirstOrDefault(p => p.Id == projectId);
        if (proj is null) return;
        var recipe = MilitaryRecipes.Get(proj.RecipeId);
        // Refund 50%
        nation.Wood += recipe.WoodCost * 0.5;
        nation.Stone += recipe.StoneCost * 0.5;
        nation.Iron += recipe.IronCost * 0.5;
        nation.Copper += recipe.CopperCost * 0.5;
        nation.Lead += recipe.LeadCost * 0.5;
        nation.MilitaryCraftQueue.Remove(proj);
        State.Log($"Cancelled crafting {recipe.Name} (50% refunded).");
        StateChanged?.Invoke();
    }

    /// <summary>Sells all stockpiled goods for gold (the player's meaningful trade action).</summary>
    public void SellGoods()
    {
        var n = State.PlayerNation;
        if (n.Goods <= 0) return;
        double gold = n.Goods * Balance.GoodsSellPrice;
        State.Log($"Sold {n.Goods:N0} goods for {Currency.Cost(gold)}.");
        n.Gold += gold;
        n.Goods = 0;
        StateChanged?.Invoke();
    }

    // ---------------- Warfare ----------------

    /// <summary>
    /// Launches an invasion: the army marches on the target nation and the
    /// battle resolves on arrival — the winner then chooses: annex the country, take its resources, or let it go.
    /// Returns (ok, message): an error, or a march confirmation.
    /// </summary>
    public (bool ok, string message) LaunchInvasion(string nationId, int commitCount)
    {
        var player = State.PlayerNation;
        var owner = State.OtherNations.FirstOrDefault(n => n.Id == nationId);
        if (owner is null) return (false, "Nation not found.");
        if (owner.IsEliminated) return (false, "That nation no longer exists.");
        if (!owner.AtWarWithPlayer) return (false, "You must declare war first.");
        if (TreatyService.AnnexationBlock(State, owner.Id) is { } protectedBy)
            return (false, $"{protectedBy} An invasion could not take the country.");
        if (player.Soldiers < Balance.MinInvasionForce)
            return (false, $"Need at least {Balance.MinInvasionForce} soldiers to invade.");

        int commit = Math.Min(commitCount, player.Soldiers);
        var force = ArmyHelper.ExtractSoldiers(player, commit);
        int days = Warfare.TravelDays(player.MapX, player.MapY, owner.MapX, owner.MapY);
        State.MarchingArmies.Add(new MarchingArmy
        {
            AttackerNationId = player.Id,
            AttackerNationName = player.Name,
            TargetNationId = owner.Id,
            TargetNationName = owner.Name,
            Force = force,
            DaysLeft = days,
            TotalDays = days,
        });

        State.LogMovement(MovementKind.March, MovementStatus.UnderWay, player, owner, $"⚔ {commit:N0} soldiers march on {owner.Name} — arrival in {days} days.");
        StateChanged?.Invoke();
        return (true, $"Your army marches on {owner.Name} — arrival in {days} days.");
    }

    // ---------------- Laws & religion ----------------

    public string? ToggleEdict(EdictType edict)
    {
        var n = State.PlayerNation;
        if (n.HasEdict(edict))
        {
            n.ActiveEdicts.Remove(edict);
            State.Log($"Repealed {EdictCatalog.Get(edict).Name}.");
        }
        else
        {
            if (!n.CanPay(Balance.EdictEnactCost)) return "Not enough Gold.";
            n.PayGold(Balance.EdictEnactCost);
            n.ActiveEdicts.Add(edict);
            State.Log($"Enacted {EdictCatalog.Get(edict).Name}.");
        }
        StateChanged?.Invoke();
        return null;
    }

    public string? SetStance(ReligiousStance stance)
    {
        var n = State.PlayerNation;
        if (n.Stance == stance) return null;
        if (!n.CanPay(Balance.StanceChangeCost)) return "Not enough Gold.";
        n.PayGold(Balance.StanceChangeCost);
        n.Stance = stance;
        State.Log($"The court adopts a {stance.ToString().ToLower()} religious stance.");
        StateChanged?.Invoke();
        return null;
    }

    // ---------------- Colonisation ----------------

    /// <summary>Sends a colony expedition to an uncharted region.</summary>
    public string? FoundColony(string regionId)
    {
        var region = State.FrontierRegions.FirstOrDefault(r => r.Id == regionId);
        if (region is null) return "Region not found.";
        if (State.ActiveExpedition is not null) return "An expedition is already at sea.";
        var n = State.PlayerNation;
        if (n.Warships < Balance.ColonyWarshipsRequired)
            return $"Need {Balance.ColonyWarshipsRequired} warships to carry the colonists.";
        if (!n.CanPay(Balance.ColonyCostGold)) return "Not enough Gold.";
        if (n.GetProduct("Wheat") < Balance.ColonyCostFood) return "Not enough Wheat for the voyage.";
        if (n.Population < Balance.ColonyColonists) return "Not enough people to spare.";

        n.PayGold(Balance.ColonyCostGold);
        n.AddProduct("Wheat", -Balance.ColonyCostFood);
        n.Population -= Balance.ColonyColonists;
        State.ActiveExpedition = new ColonyExpedition
        {
            RegionId = region.Id,
            RegionName = region.Name,
            DaysLeft = Balance.ColonyDays,
            TotalDays = Balance.ColonyDays,
        };
        State.LogMovement(MovementKind.Colony, MovementStatus.UnderWay, n.Id, n.Name, region.Id, region.Name, $"A colony expedition sails for {region.Name} ({Balance.ColonyDays} days).");
        StateChanged?.Invoke();
        return null;
    }

    // ---------------- Military ----------------

    /// <summary>Recruits land units instantly. Returns an error message, or null on success.</summary>
    public string? Recruit(UnitType type, int count)
    {
        if (count <= 0) return "Invalid count.";
        var spec = UnitCatalog.Get(type);
        var n = State.PlayerNation;

        double mult = n.HasCommander(CommanderRole.LandCommander) ? Balance.LandCommanderRecruitMult : 1.0;
        double gold = spec.GoldCost * mult * count;
        double wood = spec.WoodCost * count;
        double iron = spec.IronCost * count;
        if (!n.CanPay(gold) || n.Wood < wood || n.Iron < iron)
            return "Not enough resources.";

        n.PayGold(gold);
        n.Wood -= wood;
        n.Iron -= iron;
        var stack = n.Units.FirstOrDefault(u => u.Type == type);
        if (stack is null) n.Units.Add(new UnitStack { Type = type, Count = count });
        else stack.Count += count;

        State.Log($"Recruited {count:N0} {spec.Name} for {Currency.Cost(gold)}.");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Builds warships instantly. Returns an error message, or null on success.</summary>
    public string? RecruitWarships(int count)
    {
        if (count <= 0) return "Invalid count.";
        var n = State.PlayerNation;

        double mult = n.HasCommander(CommanderRole.FleetCommander) ? Balance.FleetCommanderRecruitMult : 1.0;
        double gold = WarshipSpec.GoldCost * mult * count;
        double wood = WarshipSpec.WoodCost * count;
        double iron = WarshipSpec.IronCost * count;
        if (!n.CanPay(gold) || n.Wood < wood || n.Iron < iron)
            return "Not enough resources.";

        n.PayGold(gold);
        n.Wood -= wood;
        n.Iron -= iron;
        n.Warships += count;

        State.Log($"Launched {count:N0} warships for {Currency.Cost(gold)}.");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Hires a commander for a vacant role. Returns an error message, or null on success.</summary>
    public string? HireCommander(CommanderRole role)
    {
        var n = State.PlayerNation;
        if (n.HasCommander(role)) return "Role already filled.";
        var spec = CommanderCatalog.GetRole(role);
        if (!n.CanPay(spec.HireCost)) return "Not enough Gold.";

        n.PayGold(spec.HireCost);
        n.Commanders.Add(new Commander
        {
            Name = CommanderCatalog.NextCandidateName(role, n.Commanders),
            Role = role,
            DailyWage = spec.DailyWage,
        });
        State.Log($"Hired {n.Commanders.Last().Name} as {spec.Title}.");
        StateChanged?.Invoke();
        return null;
    }

    public void DismissCommander(CommanderRole role)
    {
        var n = State.PlayerNation;
        var cmd = n.Commanders.FirstOrDefault(c => c.Role == role);
        if (cmd is null) return;
        n.Commanders.Remove(cmd);
        State.Log($"Dismissed {cmd.Name}.");
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Pays accrued army maintenance immediately and restarts the 180-day
    /// cycle. Returns an error message, or null on success.
    /// </summary>
    public string? PayMaintenance()
    {
        var n = State.PlayerNation;
        if (n.UpkeepAccrued <= 0) return "Nothing due.";
        if (!n.CanPay(n.UpkeepAccrued)) return "Not enough Gold in the treasury.";

        n.PayGold(n.UpkeepAccrued);
        State.Log($"Paid army maintenance early: {Currency.Format(n.UpkeepAccrued)}.");
        n.UpkeepAccrued = 0;
        n.GraceDaysLeft = 0;
        n.NextPayday = State.CurrentDate.AddDays(Balance.PaydayIntervalDays);
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Days until the next maintenance payday (negative = overdue).</summary>
    public int DaysToPayday =>
        State.PlayerNation.NextPayday.DayNumber - State.CurrentDate.DayNumber;

    // ---------------- Diplomacy ----------------

    private Nation? FindNation(string nationId) =>
        State.OtherNations.FirstOrDefault(n => n.Id == nationId);

    /// <summary>Sends a gift: +relations for gold. Returns an error, or null on success.</summary>
    public string? SendGift(string nationId)
    {
        var n = FindNation(nationId);
        if (n is null) return "Nation not found.";
        if (n.AtWarWithPlayer) return "You are at war — they refuse your gift.";
        if (!State.PlayerNation.CanPay(Balance.GiftCost)) return "Not enough Gold.";

        State.PlayerNation.PayGold(Balance.GiftCost);
        n.RelationToPlayer = Math.Min(100, n.RelationToPlayer + Balance.GiftRelationGain);
        State.LogMovement(MovementKind.Gold, MovementStatus.Completed, State.PlayerNation, n, $"Sent a gift to {n.Name} (+{Balance.GiftRelationGain} relations).");
        StateChanged?.Invoke();
        return null;
    }

    public string? DeclareWar(string nationId)
    {
        var n = FindNation(nationId);
        if (n is null) return "Nation not found.";
        if (n.AtWarWithPlayer) return "Already at war.";
        if (TreatyService.ForbidsAttack(State, State.PlayerNation.Id, n.Id) is { } forbidden) return forbidden;

        n.AtWarWithPlayer = true;
        n.HasTradePactWithPlayer = false;
        n.RelationToPlayer = -100;
        TreatyService.OnWar(State, State.PlayerNation, n);
        State.LogMovement(MovementKind.War, MovementStatus.Completed, State.PlayerNation, n, $"You declared war on {n.Name}!");
        StateChanged?.Invoke();
        return null;
    }

    public string? SueForPeace(string nationId)
    {
        var n = FindNation(nationId);
        if (n is null) return "Nation not found.";
        if (!n.AtWarWithPlayer) return "You are not at war.";
        if (!State.PlayerNation.CanPay(Balance.PeaceTributeCost)) return "Not enough Gold for tribute.";

        State.PlayerNation.PayGold(Balance.PeaceTributeCost);
        n.AtWarWithPlayer = false;
        n.RelationToPlayer = -20;
        State.PendingVictories.RemoveAll(v => v.LoserId == n.Id);   // peace made: nothing left to decide
        State.LogMovement(MovementKind.War, MovementStatus.Completed, State.PlayerNation, n, $"You sued for peace with {n.Name} (tribute {Balance.PeaceTributeCost:N0} gold).");
        StateChanged?.Invoke();
        return null;
    }

    public string? SignTradePact(string nationId)
    {
        var n = FindNation(nationId);
        if (n is null) return "Nation not found.";
        if (n.AtWarWithPlayer) return "Cannot trade while at war.";
        if (n.HasTradePactWithPlayer) return "Pact already signed.";
        if (n.RelationToPlayer < 0) return "Relations too poor — send gifts first.";
        if (!State.PlayerNation.CanPay(Balance.TradePactFee)) return "Not enough Gold.";

        State.PlayerNation.PayGold(Balance.TradePactFee);
        n.HasTradePactWithPlayer = true;
        if (!TreatyService.Has(State, TreatyType.TradeAgreement, State.PlayerNation.Id, n.Id))
            TreatyService.Add(State, TreatyType.TradeAgreement, State.PlayerNation, n);
        State.LogMovement(MovementKind.Treaty, MovementStatus.Completed, State.PlayerNation, n, $"Signed a trade pact with {n.Name}.");
        StateChanged?.Invoke();
        return null;
    }

    public void CancelTradePact(string nationId)
    {
        var n = FindNation(nationId);
        if (n is null || !n.HasTradePactWithPlayer) return;
        n.HasTradePactWithPlayer = false;
        var treaty = TreatyService.Find(State, TreatyType.TradeAgreement, State.PlayerNation.Id, n.Id);
        if (treaty is not null) TreatyService.Remove(State, treaty);
        State.LogMovement(MovementKind.Treaty, MovementStatus.Completed, State.PlayerNation, n, $"Cancelled the trade pact with {n.Name}.");
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Demands tribute. Paid if your army is 1.5x theirs; otherwise relations
    /// suffer and they may declare war. Returns an error, or null on success.
    /// </summary>
    public string? DemandTribute(string nationId)
    {
        var n = FindNation(nationId);
        if (n is null) return "Nation not found.";
        if (n.AtWarWithPlayer) return "You are already at war.";
        if (TreatyService.ForbidsAttack(State, State.PlayerNation.Id, n.Id) is { } forbidden) return forbidden;

        var player = State.PlayerNation;
        if (player.Soldiers > n.Soldiers * Balance.TributeArmyRatio)
        {
            double tribute = Math.Min(n.Gold,
                Math.Max(100, n.Gold * Balance.TributeFraction));
            n.PayGold(tribute);
            player.Gold += tribute;
            n.RelationToPlayer = Math.Max(-100, n.RelationToPlayer - 20);
            State.LogMovement(MovementKind.Gold, MovementStatus.Completed, n, player, $"{n.Name} paid tribute: {Currency.Cost(tribute)}.", inbox: InboxTopic.Aid);
        }
        else
        {
            n.RelationToPlayer = Math.Max(-100, n.RelationToPlayer - 30);
            State.LogMovement(MovementKind.Gold, MovementStatus.Failed, n, player, $"{n.Name} refused your tribute demand.", inbox: InboxTopic.Aid);
            if (_sim.RollChance(Balance.TributeRefusalWarChance))
            {
                n.AtWarWithPlayer = true;
                n.HasTradePactWithPlayer = false;
                n.RelationToPlayer = -100;
                TreatyService.OnWar(State, player, n);
                State.LogMovement(MovementKind.War, MovementStatus.Completed, n, player, $"{n.Name} declared war over your insult!", inbox: InboxTopic.WarDeclared);
                State.ActiveWarnings.Add($"⚠ {n.Name} has DECLARED WAR on you!");
                TreatyService.AllianceDefence(State, n, player);
            }
        }
        StateChanged?.Invoke();
        return null;
    }

    // ---------------- Espionage ----------------

    public SpyNetwork? GetNetwork(string nationId) =>
        State.SpyNetworks.FirstOrDefault(s => s.TargetNationId == nationId);

    public string? EstablishNetwork(string nationId)
    {
        var n = FindNation(nationId);
        if (n is null) return "Nation not found.";
        var net = GetNetwork(nationId);
        if (net is not null && net.Strength >= Balance.MaxNetworkStrength)
            return "Network already at full strength.";
        if (!State.PlayerNation.CanPay(Balance.EstablishNetworkCost)) return "Not enough Gold.";

        State.PlayerNation.PayGold(Balance.EstablishNetworkCost);
        if (net is null)
            State.SpyNetworks.Add(new SpyNetwork
            {
                TargetNationId = n.Id,
                TargetNationName = n.Name,
                Strength = Balance.EstablishNetworkStrength,
            });
        else
            net.Strength = Math.Min(Balance.MaxNetworkStrength,
                net.Strength + Balance.EstablishNetworkStrength);
        State.LogMovement(MovementKind.Mission, MovementStatus.Completed, State.PlayerNation, n, $"Spy network operating in {n.Name}.", inbox: InboxTopic.SpyNetwork);

        // The new network's first report: what the spies count in the country (real figures, rounded as spies would).
        double treasury = Math.Round(n.Gold / 1000) * 1000;
        InboxService.Notify(State, InboxTopic.ForeignStrength,
            $"{n.Name} fields about {n.Soldiers:N0} soldiers and {n.Warships:N0} warships; its treasury holds about {Currency.Cost(treasury)}.",
            n.Id,
            details: $"Report from our spy network in {n.Name}:\nArmy: about {n.Soldiers:N0} soldiers.\nFleet: {n.Warships:N0} warships.\nTreasury: about {Currency.Cost(treasury)}.\nAt war with you: {(n.AtWarWithPlayer ? "yes" : "no")}.");
        StateChanged?.Invoke();
        return null;
    }

    private string? SpendNetwork(string nationId, int minStrength, int cost,
        out SpyNetwork? net, out Nation? target)
    {
        net = null;
        target = FindNation(nationId);
        if (target is null) return "Nation not found.";
        if (TreatyService.ForbidsAttack(State, State.PlayerNation.Id, nationId) is { } forbidden) return forbidden;
        net = GetNetwork(nationId);
        if (net is null || net.Strength < minStrength)
            return $"Need a spy network of strength {minStrength}.";
        net.Strength -= cost;
        return null;
    }

    private void Discover(SpyNetwork net, Nation target, string deed)
    {
        net.Strength /= 2;
        target.RelationToPlayer = Math.Max(-100,
            target.RelationToPlayer - Balance.DiscoveryRelationHit);
        State.LogMovement(MovementKind.Mission, MovementStatus.Failed, State.PlayerNation, target, $"Our spy was caught ({deed}) in {target.Name}!", inbox: InboxTopic.SpyCaught);
        State.ActiveWarnings.Add($"🕵 Our spy was caught in {target.Name}!");
    }

    /// <summary>Steals 5–15% of the target's treasury. 25% discovery risk.</summary>
    public string? SpySteal(string nationId)
    {
        var err = SpendNetwork(nationId, Balance.StealMinStrength, Balance.StealStrengthCost,
            out var net, out var target);
        if (err is not null) return err;

        double frac = Balance.StealFractionMin
            + _sim.NextDouble() * (Balance.StealFractionMax - Balance.StealFractionMin);
        double amount = target!.Gold * frac;
        target.PayGold(amount);
        State.PlayerNation.Gold += amount;
        State.LogMovement(MovementKind.Gold, MovementStatus.Completed, target, State.PlayerNation, $"Spies stole {Currency.Cost(amount)} from {target.Name}.", inbox: InboxTopic.SpyMission);
        if (_sim.RollChance(Balance.StealDiscoveryChance))
            Discover(net!, target, "theft");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Destroys one random building in the target nation. 30% discovery risk.</summary>
    public string? SpySabotage(string nationId)
    {
        var err = SpendNetwork(nationId, Balance.SabotageMinStrength, Balance.SabotageStrengthCost,
            out var net, out var target);
        if (err is not null) return err;

        if (target!.Farms + target.Mines + target.Sawmills + target.Workshops == 0)
        {
            net!.Strength += Balance.SabotageStrengthCost; // refund
            return "No buildings to sabotage.";
        }

        var present = new List<(BuildingType type, string name)>();
        if (target.Farms > 0) present.Add((BuildingType.Farm, "Farm"));
        if (target.Mines > 0) present.Add((BuildingType.Mine, "Mine"));
        if (target.Sawmills > 0) present.Add((BuildingType.Sawmill, "Sawmill"));
        if (target.Workshops > 0) present.Add((BuildingType.Workshop, "Workshop"));
        var pick = present[_sim.NextInt(present.Count)];
        switch (pick.type)
        {
            case BuildingType.Farm: target.Farms--; break;
            case BuildingType.Mine: target.Mines--; break;
            case BuildingType.Sawmill: target.Sawmills--; break;
            case BuildingType.Workshop: target.Workshops--; break;
        }
        State.LogMovement(MovementKind.Mission, MovementStatus.Completed, State.PlayerNation, target, $"Spies sabotaged a {pick.name} in {target.Name}.", inbox: InboxTopic.SpyMission);
        if (_sim.RollChance(Balance.SabotageDiscoveryChance))
            Discover(net!, target, "sabotage");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Incites 5% of the target's soldiers to desert. 35% discovery risk.</summary>
    public string? SpyInciteRevolt(string nationId)
    {
        var err = SpendNetwork(nationId, Balance.InciteMinStrength, Balance.InciteStrengthCost,
            out var net, out var target);
        if (err is not null) return err;

        int deserters = (int)(target!.Soldiers * Balance.InciteDesertionFraction);
        ArmyHelper.RemoveSoldiers(target, deserters);
        State.LogMovement(MovementKind.Mission, MovementStatus.Completed, State.PlayerNation, target, $"Spies incited revolt in {target.Name}: {deserters:N0} soldiers deserted.", inbox: InboxTopic.SpyMission);
        if (_sim.RollChance(Balance.InciteDiscoveryChance))
            Discover(net!, target, "sedition");
        StateChanged?.Invoke();
        return null;
    }

    /// <summary>Advances exactly one day. Used by the clock and by tests.</summary>
    public void AdvanceOneDay() => OnDayElapsed();

    private void OnDayElapsed()
    {
        State.CurrentDate = State.CurrentDate.AddDays(1);
        _sim.AdvanceDay(State);
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clock.DayElapsed -= OnDayElapsed;
        Clock.Dispose();
    }
}
