// Stripe Custom Checkout integration for Blazor.
// Called via JS interop from Checkout.razor (InteractiveServer render mode).
//
// Uses Stripe's Custom Checkout flow (stripe.initCheckout) rather than the
// prebuilt Checkout page, giving full control over the payment UI while
// Stripe handles PCI-sensitive field rendering inside iframes.
//
// Docs: https://docs.stripe.com/custom-checkout/overview
// Appearance API: https://docs.stripe.com/elements/appearance-api

window.StripeCheckout = {
    _checkout: null,
    _paymentElement: null,
    _addressElement: null,

    // Initializes the Stripe checkout session and mounts both the Payment
    // and Shipping Address Elements into their respective DOM containers.
    // Called once from Checkout.razor after the Blazor circuit is established.
    //
    // The clientSecret comes from a server-side Stripe Checkout Session
    // created with ui_mode: "custom" (see CreateAndMountStripeSession in Checkout.razor).
    init: async function (publishableKey, clientSecret) {
        const stripe = Stripe(publishableKey);

        // Appearance tokens are matched to the WithLove design system defined
        // in the Tailwind config (App.razor). Keep these in sync if the
        // palette or typography changes.
        const appearance = {
            theme: 'stripe',
            variables: {
                colorPrimary: '#DFA8A8',        // primary (dusty rose)
                colorBackground: '#FDFBF7',     // warm white (matches page bg)
                colorText: '#4A453E',            // stone-800
                colorDanger: '#df1b41',
                colorTextSecondary: '#8C7B75',   // stone-500
                colorTextPlaceholder: '#C5BFB8', // stone-300
                fontFamily: 'Quicksand, sans-serif',
                fontSizeBase: '14px',
                fontWeightNormal: '500',
                fontWeightMedium: '600',
                fontWeightBold: '700',
                borderRadius: '12px',
                spacingUnit: '4px',
                focusBoxShadow: '0 0 0 2px rgba(223, 168, 168, 0.15)',
                focusOutline: 'none',
            },
            rules: {
                '.Label': {
                    fontSize: '11px',
                    fontWeight: '700',
                    textTransform: 'uppercase',
                    letterSpacing: '0.12em',
                    color: '#9CA3AF',
                    marginBottom: '8px',
                },
                '.Input': {
                    backgroundColor: '#FDFBF7',
                    border: '1px solid #E8E2DB',
                    borderRadius: '12px',
                    padding: '12px 16px',
                    fontSize: '14px',
                    transition: 'border-color 0.2s, box-shadow 0.2s',
                },
                '.Input:focus': {
                    borderColor: '#DFA8A8',
                    boxShadow: '0 0 0 2px rgba(223, 168, 168, 0.1)',
                },
                '.Input--invalid': {
                    borderColor: '#df1b41',
                    boxShadow: '0 0 0 2px rgba(223, 27, 65, 0.1)',
                },
                '.Tab': {
                    border: '1px solid #E8E2DB',
                    borderRadius: '12px',
                    backgroundColor: '#FDFBF7',
                },
                '.Tab:hover': {
                    borderColor: '#C58B8B',       // primary-dark
                },
                '.Tab--selected': {
                    borderColor: '#DFA8A8',
                    boxShadow: '0 0 0 2px rgba(223, 168, 168, 0.15)',
                    backgroundColor: '#ffffff',
                },
                '.Error': {
                    fontSize: '12px',
                    marginTop: '4px',
                },
            },
        };
        const checkout = await stripe.initCheckout({
            clientSecret: clientSecret,
            elementsOptions: {appearance}
        });

        // Payment Element: renders card/wallet inputs inside a Stripe-hosted iframe.
        // Mounts into #payment-element defined in Checkout.razor Step 3.
        const paymentElement = checkout.createPaymentElement();
        paymentElement.mount('#payment-element');

        // Shipping Address Element: provides address autocomplete (25+ countries)
        // and syncs collected data directly into the Checkout Session — no manual
        // updateShippingAddress() call needed on confirm.
        // Mounts into #shipping-address-element defined in Checkout.razor Step 1.
        // Note: createShippingAddressElement() accepts NO options (unlike
        // elements.create("address", ...) in the Elements API). Allowed countries
        // are configured server-side via ShippingAddressCollection on the Session.
        const addressElement = checkout.createShippingAddressElement();
        addressElement.mount('#shipping-address-element');

        this._checkout = checkout;
        this._paymentElement = paymentElement;
        this._addressElement = addressElement;
    },

    // Confirms the payment. Called from HandleValidSubmit in Checkout.razor
    // after Blazor-side form validation passes.
    //
    // loadActions() resolves available actions for the current session state.
    // actions.confirm() triggers Stripe's client-side validation on all mounted
    // Elements (address + payment). If validation fails, Stripe shows inline
    // errors and returns an error object. On success, Stripe auto-redirects
    // to the return_url configured on the Checkout Session (order-confirmation page).
    //
    // Returns { success: bool, error: string|null } for Blazor interop.
    confirm: async function () {
        if (!this._checkout) {
            return { success: false, error: 'Payment not initialized.' };
        }
        try {
            const { actions, type } = await this._checkout.loadActions();
            if (type !== 'success') {
                return { success: false, error: 'failed to load actions' };
            }

            const error = await actions.confirm();

            if (error) {
                return { success: false, error: error.message };
            }
            return { success: true, error: null };
        } catch (e) {
            return { success: false, error: e.message || 'Payment confirmation failed.' };
        }
    },

    // Tears down Stripe Elements and releases resources.
    // Called from Checkout.razor's Dispose (IAsyncDisposable) when the user
    // navigates away or the Blazor circuit disconnects.
    destroy: function () {
        if (this._addressElement) {
            this._addressElement.destroy();
            this._addressElement = null;
        }
        if (this._paymentElement) {
            this._paymentElement.destroy();
            this._paymentElement = null;
        }
        if (this._checkout) {
            this._checkout = null;
        }
    }
};
