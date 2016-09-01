﻿#region using directives

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.PoGoUtils;
using PoGo.NecroBot.Logic.State;
using PoGo.NecroBot.Logic.Utils;
using PoGo.NecroBot.Logic.Logging;

#endregion

namespace PoGo.NecroBot.Logic.Tasks
{
    public class TransferDuplicatePokemonTask
    {
        public static async Task Execute(ISession session, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await session.Inventory.RefreshCachedInventory();
            var duplicatePokemons =
                await
                    session.Inventory.GetDuplicatePokemonToTransfer(
                        session.LogicSettings.PokemonsNotToTransfer,
                        session.LogicSettings.PokemonsToEvolve, 
                        session.LogicSettings.KeepPokemonsThatCanEvolve,
                        session.LogicSettings.PrioritizeIvOverCp);

            var orderedPokemon = duplicatePokemons.OrderBy( poke => poke.Cp );

            var pokemonSettings = await session.Inventory.GetPokemonSettings();
            var pokemonFamilies = await session.Inventory.GetPokemonFamilies();

            foreach (var duplicatePokemon in orderedPokemon)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Padding the TransferEvent with player-choosen delay before instead of after.
                // This is to remedy too quick transfers, often happening within a second of the
                // previous action otherwise

                DelayingUtils.Delay(session.LogicSettings.DelayBetweenPlayerActions, 0);

                await session.Client.Inventory.TransferPokemon(duplicatePokemon.Id);
                await session.Inventory.DeletePokemonFromInvById(duplicatePokemon.Id);

                var bestPokemonOfType = (session.LogicSettings.PrioritizeIvOverCp
                    ? await session.Inventory.GetHighestPokemonOfTypeByIv(duplicatePokemon)
                    : await session.Inventory.GetHighestPokemonOfTypeByCp(duplicatePokemon)) ?? duplicatePokemon;

                var setting = pokemonSettings.SingleOrDefault(q => q.PokemonId == duplicatePokemon.PokemonId);
                var family = pokemonFamilies.FirstOrDefault(q => q.FamilyId == setting.FamilyId);

                family.Candy_++;

                Logger.Write($"Transfer Duplicate => CP:{duplicatePokemon.Cp} IV:{PokemonInfo.CalculatePokemonPerfection(duplicatePokemon).ToString("0.00")}"
                    + $"  Move : {duplicatePokemon.Move1} , {duplicatePokemon.Move2}"
                    , LogLevel.Transfer);

                session.EventDispatcher.Send(new TransferPokemonEvent
                {
                    Id = duplicatePokemon.PokemonId,
                    Perfection = PokemonInfo.CalculatePokemonPerfection(duplicatePokemon),
                    Cp = duplicatePokemon.Cp,
                    BestCp = bestPokemonOfType.Cp,
                    BestPerfection = PokemonInfo.CalculatePokemonPerfection(bestPokemonOfType),
                    FamilyCandies = family.Candy_
                });

            }
        }
    }
}