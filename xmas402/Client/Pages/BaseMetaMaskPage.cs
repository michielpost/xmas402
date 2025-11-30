using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.HostWallet;
using Nethereum.UI;
using System.Security.Claims;
using x402.Client.Events;
using x402.Client.EVM;
using x402.Client.v1;
using x402.Core;
using x402.Core.Interfaces;
using x402.Core.Models.v1;

namespace xmas402.Client.Pages;

public class BaseMetaMaskPage : ComponentBase, IDisposable
{
    [Inject]
    protected SelectedEthereumHostProviderService selectedHostProviderService { get; set; } = default!;

    [Inject]
    protected IWalletProvider WalletProvider { get; set; } = default!;

    [Inject]
    protected IAssetInfoProvider AssetInfoProvider { get; set; } = default!;

    [CascadingParameter]
    public Task<AuthenticationState> AuthenticationState { get; set; } = default!;

    protected string? SelectedAccount { get; set; }
    protected string? UserName { get; set; }
    protected long SelectedChainId { get; set; }
    protected IEthereumHostProvider? _ethereumHostProvider;

    protected override void OnInitialized()
    {
        Console.WriteLine("OnInitialized??");

        if (OperatingSystem.IsBrowser())
        {
            //metamask is selected
            _ethereumHostProvider = selectedHostProviderService.SelectedHost;
            _ethereumHostProvider.SelectedAccountChanged += HostProvider_SelectedAccountChanged;

            WalletProvider.PrepareWallet += WalletProvider_PrepareWallet;
        }
    }

    public virtual void Dispose()
    {
        if (_ethereumHostProvider != null)
        {
            _ethereumHostProvider.SelectedAccountChanged -= HostProvider_SelectedAccountChanged;

            WalletProvider.PrepareWallet -= WalletProvider_PrepareWallet;
        }
    }

    protected override async Task OnInitializedAsync()
    {
        if (OperatingSystem.IsBrowser())
        {
            if (_ethereumHostProvider != null)
            {
                var ethereumAvailable = await _ethereumHostProvider.CheckProviderAvailabilityAsync();
                if (ethereumAvailable)
                {
                    SelectedAccount = await _ethereumHostProvider.GetProviderSelectedAccountAsync();
                }
            }

            Console.WriteLine("OK??");


            var authState = await AuthenticationState;
            if (authState != null)
            {
                Console.WriteLine("It's here");
                UserName = authState.User.FindFirst(c => c.Type.Contains(ClaimTypes.NameIdentifier))?.Value;
            }
        }
    }

    protected virtual async Task HostProvider_SelectedAccountChanged(string account)
    {
        SelectedAccount = account;
        this.StateHasChanged();
    }

    private async Task<bool> WalletProvider_PrepareWallet(object? sender, PrepareWalletEventArgs<PaymentRequiredResponse> eventArgs)
    {
        var networks = eventArgs.PaymentRequiredResponse.Accepts.Select(x => x.Network).Distinct();

        var network = networks.First();

        //Get known assets
        var assetInfo = AssetInfoProvider.GetAssetInfoByNetwork(network);
        if (assetInfo == null)
        {
            return false;
        }


        if (_ethereumHostProvider == null)
        {
            return false;
        }

        var web3 = await _ethereumHostProvider.GetWeb3Async();

        var chainId = new HexBigInteger(assetInfo.ChainId);

        var changeResult = await ChangeChainTo(assetInfo.ChainId);
        if (!string.IsNullOrEmpty(changeResult))
        {
            return false;
        }

        var selectedChainId = await web3.Eth.ChainId.SendRequestAsync();
        if (selectedChainId.Value != chainId.Value)
        {
            return false;
        }

        if (SelectedAccount == null)
        {
            return false;
        }

        var wallet = new EVMWallet((s) => web3.Eth.AccountSigning.SignTypedDataV4.SendRequestAsync(s), SelectedAccount, assetInfo.Network, assetInfo.ChainId)
        {
            IgnoreAllowances = true
        };
        WalletProvider.Wallet = wallet;

        return true;
    }

    protected async Task<string> ChangeChainTo(ulong chainId)
    {
        if (_ethereumHostProvider == null)
            return "_ethereumHostProvider is null";

        try
        {
            var web3 = await _ethereumHostProvider.GetWeb3Async();
            var result = await web3.Eth.HostWallet.SwitchEthereumChain.SendRequestAsync(new SwitchEthereumChainParameter() { ChainId = new HexBigInteger(chainId) });
            return result;
        }
        catch (Exception ex)
        {
            return $"Error chaing MetaMask chain to {chainId}: {ex.Message}";
        }
    }
}
