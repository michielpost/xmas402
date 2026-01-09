using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.HostWallet;
using Nethereum.UI;
using System.Security.Claims;
using x402.Client.Events;
using x402.Client.EVM;
using x402.Client.v2;
using x402.Core.Interfaces;
using x402.Core.Models.v2;

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

    public string Progress { get; set; } = string.Empty;

    protected override void OnInitialized()
    {
        Console.WriteLine("OnInitialized??");

        if (OperatingSystem.IsBrowser())
        {
            //metamask is selected
            _ethereumHostProvider = selectedHostProviderService.SelectedHost;
            _ethereumHostProvider.SelectedAccountChanged += HostProvider_SelectedAccountChanged;

            WalletProvider.PrepareWallet += WalletProvider_PrepareWallet;
            WalletProvider.PaymentSelected += WalletProvider_PaymentSelected;
            WalletProvider.HeaderCreated += WalletProvider_HeaderCreated;
        }
    }

    public virtual void Dispose()
    {
        if (_ethereumHostProvider != null)
        {
            _ethereumHostProvider.SelectedAccountChanged -= HostProvider_SelectedAccountChanged;

            WalletProvider.PrepareWallet -= WalletProvider_PrepareWallet;
            WalletProvider.PaymentSelected -= WalletProvider_PaymentSelected;
            WalletProvider.HeaderCreated -= WalletProvider_HeaderCreated;
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

            var authState = await AuthenticationState;
            if (authState != null)
            {
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
        SetProgress("Preparing wallet for x402 payment requirements");

        var networks = eventArgs.PaymentRequiredResponse.Accepts.Select(x => x.Network).Distinct();

        if (networks.Count() > 1)
        {
            SetProgress($"Received x402 requirements for these networks: {string.Join(", ", networks)}");
        }
        else
        {
            SetProgress($"Received x402 requirements for network: {string.Join(", ", networks)}");
        }

        var network = networks.First();

        //Get known assets
        var assetInfos = AssetInfoProvider.GetAssetInfoByNetwork(network);
        var assetInfo = assetInfos.FirstOrDefault();
        if (assetInfo != null)
        {
            SetProgress($"Known network: {assetInfo.Network} ({assetInfo.ChainId})");
        }
        else
        {
            SetProgress($"Unknown network: {network}");
            return false;
        }

        if (_ethereumHostProvider == null)
        {
            SetProgress($"Enable MetaMask to proceed.");

            return false;
        }

        var web3 = await _ethereumHostProvider.GetWeb3Async();

        var chainId = new HexBigInteger(assetInfo.ChainId);

        var changeResult = await ChangeChainTo(assetInfo.ChainId);
        if (!string.IsNullOrEmpty(changeResult))
        {
            SetProgress($"Error changing MetaMask to ChainId: {assetInfo.ChainId}: {changeResult}");
            return false;
        }
        else
        {
            SetProgress($"Changed MetaMask to ChainId: {assetInfo.ChainId}");
        }

        var selectedChainId = await web3.Eth.ChainId.SendRequestAsync();
        if (selectedChainId.Value != chainId.Value)
        {
            SetProgress($"Failed to change chainId. Need: {chainId.Value}, current: {selectedChainId}");
            return false;
        }


        if (SelectedAccount == null)
        {
            SetProgress("No account selected in MetaMask");
            return false;
        }

        var wallet = new EVMWallet((s) => web3.Eth.AccountSigning.SignTypedDataV4.SendRequestAsync(s), SelectedAccount, assetInfo.Network, assetInfo.ChainId)
        {
            IgnoreAllowances = true
        };
        WalletProvider.Wallet = wallet;

        return true;
    }

    private void WalletProvider_PaymentSelected(object? sender, PaymentSelectedEventArgs<PaymentRequirements> eventArgs)
    {
        if (eventArgs.PaymentRequirements == null)
        {
            SetProgress("No payment selected");
        }
        else
        {
            SetProgress($"Payment selected: {eventArgs.PaymentRequirements.Amount} {eventArgs.PaymentRequirements.Asset} on {eventArgs.PaymentRequirements.Network}");
        }
    }

    private void WalletProvider_HeaderCreated(object? sender, HeaderCreatedEventArgs<PaymentPayloadHeader> eventArgs)
    {
        SetProgress("Requesting with x402 header...");
        //SetProgress($"Payload: {eventArgs.PaymentPayloadHeader.ToBase64Header()}");
    }

    private void SetProgress(string msg)
    {
        Progress = msg;
        StateHasChanged();
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
