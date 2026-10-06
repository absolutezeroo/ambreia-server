<script lang="ts">
  import ConfirmReasonModal from '../components/ConfirmReasonModal.svelte';
  import PageHeader from '../components/PageHeader.svelte';
  import OpResult from '../components/OpResult.svelte';
  import { onMount } from 'svelte';
  import {
    Activity,
    ChevronRight,
    Folder,
    FolderOpen,
    Hash,
    Image,
    Package,
    Tag,
  } from '@lucide/svelte';
  import AssetImage from '../components/AssetImage.svelte';
  import CurrencyIcon from '../components/CurrencyIcon.svelte';
  import { CURRENCY_KIND, currencyChipClass, currencyKindFromRewardType } from '../lib/currency';
  import { formatNumber } from '../lib/format';
  import { apiGet, describeApiError } from '../lib/api';
  import type {
    CatalogCurrencyList,
    CatalogIconTemplate,
    CatalogOfferDetail,
    CatalogOfferRow,
    CatalogPageDetail,
    CatalogPageList,
    CatalogPageRow,
    CatalogProductRow,
    CatalogCurrency,
  } from '../lib/apiTypes';
  import type { PickerRow } from '../lib/pickers/directories';

  /** Number fields bind to inputs that hand back a string while being typed. */
  type Num = number | string;

  /**
   * A page's form. imageData/textData are stored as JSON string lists; the editor is one item
   * per line, which is why they are text here and arrays on the wire.
   */
  type PageForm = {
    localization: string;
    name: string;
    icon: Num;
    layout: string;
    sortOrder: Num;
    visible: boolean;
    imageDataText: string;
    textDataText: string;
  };

  type OfferForm = {
    localizationId: string;
    costCredits: Num;
    costCurrency: Num;
    currencyTypeId: Num;
    canGift: boolean;
    canBundle: boolean;
    clubLevel: Num;
    discountPercent: Num;
    visible: boolean;
  };

  type ProductForm = {
    productType: Num;
    furnitureDefinitionId: Num;
    furnitureName: string;
    furnitureIcon: string;
    furnitureSprite: Num;
    extraParam: string;
    quantity: Num;
    uniqueSize: Num;
    uniqueRemaining: Num;
  };

  /** One step of the breadcrumb: the page and what to call it. */
  type Crumb = { id: number; label: string };

  /** Who the picker is filling in, and what to do once it has. */
  type ProductPick = { title: string; onSelect: (row: PickerRow) => void };
  import { createWriteOps } from '../lib/writeOps';
  import { isPermissionDeniedError, hasDashboardCapability } from '../lib/permissions';
  import { CAPABILITIES } from '../lib/dashboardPermissions';
  import { nonNegative } from '../lib/validation';
  import { diffFields, type FieldChange } from '../lib/changes';
  import { PRODUCT_TYPES } from '../lib/furnitureEnums';
  import AccessDeniedNotice from '../components/AccessDeniedNotice.svelte';
  import Drawer from '../components/Drawer.svelte';
  import PickerModal from '../components/PickerModal.svelte';
  import CatalogIconPickerModal from '../components/CatalogIconPickerModal.svelte';
  import StatCard from '../components/StatCard.svelte';
  import { identity } from '../lib/session';
  import { t, translate, type Translator } from '../lib/i18n';

  const CATALOG_TYPES = [
    { value: 0, key: 'catalogAdmin.typeNormal' },
  ];

  const LAYOUT_OPTIONS = [
    'default_3x3', 'badge_display', 'club_buy', 'club_gifts', 'frontpage4', 'frontpage_featured',
    'guild_custom_furni', 'guild_forum', 'guild_frontpage', 'info_duckets', 'info_loyalty',
    'info_rentables', 'loyalty_vip_buy', 'marketplace', 'marketplace_own_items', 'monkey',
    'petcustomization', 'pets', 'pets2', 'pets3', 'recycler', 'recycler_info', 'recycler_prizes',
    'roomads', 'single_bundle', 'soundmachine', 'spaces_new', 'trophies', 'vip_buy',
    'old_layout_marketplace', 'old_layout_marketplace_own_items',
  ];

  function formatLayoutLabel(wireValue: string) {
    return wireValue
      .split('_')
      .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
      .join(' ');
  }

  function emptyPageForm(): PageForm {
    return {
      localization: '', name: '', icon: 0, layout: 'default_3x3', sortOrder: 0, visible: true,
      imageDataText: '', textDataText: '',
    };
  }

  function emptyOfferForm(): OfferForm {
    return {
      localizationId: '', costCredits: 0, costCurrency: 0, currencyTypeId: '',
      canGift: true, canBundle: true, clubLevel: 0, discountPercent: 0, visible: true,
    };
  }

  function emptyProductForm(): ProductForm {
    return {
      productType: 0, furnitureDefinitionId: '', furnitureName: '', furnitureIcon: '', furnitureSprite: '',
      extraParam: '', quantity: 1, uniqueSize: 0, uniqueRemaining: 0,
    };
  }

  // catalog_pages.image_data/text_data are stored as JSON string lists. A one-item-per-line
  // textarea is the simplest editable form for that shape -- blank lines are dropped, and an
  // empty result becomes null (matches "not set" rather than an empty array).
  function linesToArray(text: string) {
    const lines = text
      .split('\n')
      .map((line) => line.trim())
      .filter((line) => line.length > 0);
    return lines.length > 0 ? lines : null;
  }

  function arrayToLines(value: readonly string[] | null | undefined) {
    return Array.isArray(value) ? value.join('\n') : '';
  }

  let catalogType = $state(0);
  /** Ancestors of the current level; empty at the root. */
  let parentChain = $state<Crumb[]>([]);

  let pages = $state<CatalogPageRow[]>([]);
  let pagesLoading = $state(false);
  let pagesError = $state('');
  let pagesForbidden = $state(false);

  let currentPage = $state<CatalogPageDetail | null>(null);

  let currencyTypes = $state<CatalogCurrency[]>([]);
  // An offer's currencyTypeId is the activity-point type (5 = diamonds), which is what the purchase
  // debits and the client reads -- not a currency row id. Only activity-point currencies have one.
  let activityPointCurrencies = $derived(currencyTypes.filter((c) => c.activityPointType != null));
  let iconTemplate: string | null = '';

  // 'new' | 'edit' | null -- which form's icon field the picker modal is currently targeting.
  let iconPickerTarget = $state<'new' | 'edit' | null>(null);

  function iconUrlFor(id: Num) {
    return iconTemplate && Number(id) > 0 ? iconTemplate.replace('{id}', String(id)) : null;
  }

  let newPageOpen = $state(false);
  let newPage = $state(emptyPageForm());
  let editPageOpen = $state(false);
  let editPageForm = $state<PageForm | null>(null);

  let newOfferOpen = $state(false);
  let newOffer = $state(emptyOfferForm());
  let editOfferId = $state<number | null>(null);
  let editOfferForm = $state<OfferForm | null>(null);

  let selectedOfferId = $state<number | null>(null);
  let offerDetail = $state<CatalogOfferDetail | null>(null);
  let offerDetailLoading = $state(false);
  let offerDetailError = $state('');

  let newProductOpen = $state(false);
  let newProduct = $state(emptyProductForm());
  let editProductId = $state<number | null>(null);
  let editProductForm = $state<ProductForm | null>(null);

  // Every write is staged here and confirmed in the dialog below before it is posted. createWriteOps
  // owns that cycle -- posting, remembering the audited reason, and tracking each form's busy state,
  // error and result under its own key -- so the page only describes what each button writes.
  const ops = createWriteOps();
  let picker = $state<ProductPick | null>(null);

  let canManage = $derived(hasDashboardCapability($identity, CAPABILITIES.opsCatalogManage));

  // Plain function, not a `$:` derived value: navigation functions below mutate `parentChain` and
  // then immediately call loadPages()/loadCurrentPage() in the same synchronous block. A `$:`
  // reactive statement would not have recomputed yet at that point (Svelte batches reactive
  // updates after the current call stack), so those calls would read a stale parentId.
  function currentParentId() {
    return parentChain.length > 0 ? parentChain[parentChain.length - 1].id : null;
  }

  async function loadPages() {
    pagesLoading = true;
    pagesError = '';
    pagesForbidden = false;

    const parentId = currentParentId();
    const params = new URLSearchParams({ catalogType: String(catalogType) });
    if (parentId !== null) params.set('parentId', String(parentId));

    try {
      const data = await apiGet<CatalogPageList>(`/api/v1/catalog/pages?${params}`);
      pages = data.items || [];
    } catch (err) {
      if (isPermissionDeniedError(err)) {
        pagesForbidden = true;
        pages = [];
        return;
      }

      pagesError = (err as Error).message;
      pages = [];
    } finally {
      pagesLoading = false;
    }
  }

  async function loadCurrentPage() {
    const parentId = currentParentId();

    if (parentId === null) {
      currentPage = null;
      return;
    }

    try {
      currentPage = await apiGet<CatalogPageDetail>(`/api/v1/catalog/pages/${parentId}`);
    } catch (err) {
      currentPage = null;
      pagesError = isPermissionDeniedError(err)
        ? translate('common.insufficientRights')
        : describeApiError(err);
    }
  }

  async function loadOfferDetail(offerId: number) {
    selectedOfferId = offerId;
    offerDetail = null;
    offerDetailError = '';
    offerDetailLoading = true;
    newProductOpen = false;
    editProductId = null;

    try {
      offerDetail = await apiGet<CatalogOfferDetail>(`/api/v1/catalog/offers/${offerId}`);
    } catch (err) {
      offerDetailError = isPermissionDeniedError(err)
        ? translate('common.insufficientRights')
        : describeApiError(err);
    } finally {
      offerDetailLoading = false;
    }
  }

  // An "offer" (the priced catalog listing) and its "product(s)" (what actually gets delivered)
  // are two different DB rows, but almost every offer has exactly one product -- calling the
  // expand button "Products" on a row that already reads as "one item" is confusing. Label it by
  // what's actually there instead. `expandedOfferId` is passed in (rather than read from the
  // outer `selectedOfferId` inside the function body) so this stays part of the template
  // expression Svelte's reactivity tracks -- a value only read inside a called function's body
  // wouldn't be seen as a dependency of the {expression} it's called from.
  function offerActionLabel(
    offer: CatalogOfferRow,
    expandedOfferId: number | null,
    translator: Translator,
  ) {
    const expanded = expandedOfferId === offer.id;

    if (offer.productCount === 0) {
      return expanded ? translator('catalogAdmin.hide') : translator('catalogAdmin.addItem');
    }

    if (offer.productCount === 1) {
      return expanded ? translator('catalogAdmin.hideDetails') : translator('catalogAdmin.manage');
    }

    return expanded ? translator('catalogAdmin.hideItems') : translator('catalogAdmin.itemsCount', { count: offer.productCount });
  }

  // Toggles the inline products panel under the clicked offer's own card -- clicking the same
  // offer's action button again collapses it instead of re-fetching.
  async function toggleOfferDetail(offerId: number) {
    if (selectedOfferId === offerId) {
      selectedOfferId = null;
      offerDetail = null;
      return;
    }

    await loadOfferDetail(offerId);
  }

  async function refreshAll() {
    await loadPages();
    await loadCurrentPage();
    if (selectedOfferId) {
      await loadOfferDetail(selectedOfferId);
    }
  }

  function switchCatalogType(value: number) {
    if (catalogType === value) return;
    catalogType = value;
    parentChain = [];
    currentPage = null;
    selectedOfferId = null;
    offerDetail = null;
    newPageOpen = false;
    editPageOpen = false;
    void loadPages();
  }

  function drillInto(page: CatalogPageRow) {
    parentChain = [...parentChain, { id: page.id, label: page.localization }];
    selectedOfferId = null;
    offerDetail = null;
    newPageOpen = false;
    editPageOpen = false;
    void loadPages();
    void loadCurrentPage();
  }

  function drillToBreadcrumb(index: number) {
    // index -1 = root.
    parentChain = index < 0 ? [] : parentChain.slice(0, index + 1);
    selectedOfferId = null;
    offerDetail = null;
    newPageOpen = false;
    editPageOpen = false;
    void loadPages();
    void loadCurrentPage();
  }

  function goUp() {
    drillToBreadcrumb(parentChain.length - 2);
  }

  // `changes` is the before/after list for an edit (empty for a create or a delete). It is shown in
  // the confirm dialog and becomes the audited reason, which is why none of these actions asks the
  // operator to type one any more: the page already knows what it is about to do.
  const stage = (
    id: string,
    title: string,
    endpoint: string,
    valid: boolean,
    body: Record<string, unknown>,
    summary: string,
    onSuccess: () => void | Promise<void>,
    changes: FieldChange[] = [],
  ) =>
    ops.ask(endpoint, body, title, summary, {
      key: id,
      valid,
      invalidMessage: translate('catalogAdmin.fillFields'),
      changes,
      onSuccess,
    });

  // The fields worth naming in an audit line, in the order an operator reads them. Short labels on
  // purpose -- the form's own "Cost (credits) *" reads badly inside a sentence.
  const OFFER_FIELDS = () => [
    { key: 'localizationId', label: translate('catalogAdmin.fieldLocalizationId') },
    { key: 'costCredits', label: translate('catalogAdmin.fieldCostCredits') },
    { key: 'costCurrency', label: translate('catalogAdmin.fieldCostCurrency') },
    { key: 'currencyTypeId', label: translate('catalogAdmin.fieldCurrencyType') },
    { key: 'canGift', label: translate('catalogAdmin.fieldCanGift') },
    { key: 'canBundle', label: translate('catalogAdmin.fieldCanBundle') },
    { key: 'clubLevel', label: translate('catalogAdmin.fieldClubLevel') },
    { key: 'discountPercent', label: translate('catalogAdmin.fieldDiscount') },
    { key: 'visible', label: translate('catalogAdmin.fieldVisible') },
  ];

  const PAGE_FIELDS = () => [
    { key: 'localization', label: translate('catalogAdmin.fieldLocalization') },
    { key: 'name', label: translate('catalogAdmin.fieldName') },
    { key: 'icon', label: translate('catalogAdmin.fieldIcon') },
    { key: 'layout', label: translate('catalogAdmin.fieldLayout') },
    { key: 'sortOrder', label: translate('catalogAdmin.fieldSortOrder') },
    { key: 'visible', label: translate('catalogAdmin.fieldVisible') },
  ];

  const PRODUCT_FIELDS = () => [
    { key: 'productType', label: translate('catalogAdmin.fieldProductType') },
    { key: 'furnitureDefinitionId', label: translate('catalogAdmin.fieldFurniture') },
    { key: 'extraParam', label: translate('catalogAdmin.fieldExtraParam') },
    { key: 'quantity', label: translate('catalogAdmin.fieldQuantity') },
    { key: 'uniqueSize', label: translate('catalogAdmin.fieldUniqueSize') },
    { key: 'uniqueRemaining', label: translate('catalogAdmin.fieldUniqueRemaining') },
  ];

  function stageCreatePage() {
    if (!canManage) {
      ops.fail('createPage', translate('common.insufficientRights'));
      return;
    }

    const parentId = currentParentId();

    stage(
      'createPage',
      translate('catalogAdmin.newPage'),
      '/api/v1/operations/catalog/pages',
      Boolean(newPage.localization.trim()),
      {
        catalogType,
        parentId,
        localization: newPage.localization.trim(),
        name: newPage.name.trim() || null,
        icon: Number(newPage.icon) || 0,
        layout: newPage.layout,
        imageData: linesToArray(newPage.imageDataText),
        textData: linesToArray(newPage.textDataText),
        sortOrder: Number(newPage.sortOrder) || 0,
        visible: newPage.visible,
      },
      translate('catalogAdmin.createPageSummary', { name: newPage.localization.trim(), parent: parentId ? `#${parentId}` : translate('catalogAdmin.root').toLowerCase() }),
      async () => {
        newPageOpen = false;
        newPage = emptyPageForm();
        await loadPages();
      },
    );
  }

  function startEditPage() {
    if (!currentPage) return;
    editPageForm = {
      localization: currentPage.localization,
      name: currentPage.name || '',
      icon: currentPage.icon,
      layout: currentPage.layout,
      sortOrder: currentPage.sortOrder,
      visible: currentPage.visible,
      imageDataText: arrayToLines(currentPage.imageData),
      textDataText: arrayToLines(currentPage.textData),
    };
    editPageOpen = true;
  }

  function stageUpdatePage() {
    if (!canManage || !currentPage || !editPageForm) return;

    const form = editPageForm;

    stage(
      'updatePage',
      translate('catalogAdmin.edit'),
      '/api/v1/operations/catalog/pages/update',
      Boolean(editPageForm.localization.trim()),
      {
        pageId: currentPage.id,
        parentId: currentPage.parentEntityId,
        localization: editPageForm.localization.trim(),
        name: editPageForm.name.trim() || null,
        icon: Number(editPageForm.icon) || 0,
        layout: editPageForm.layout,
        imageData: linesToArray(editPageForm.imageDataText),
        textData: linesToArray(editPageForm.textDataText),
        sortOrder: Number(editPageForm.sortOrder) || 0,
        visible: editPageForm.visible,
      },
      translate('catalogAdmin.updatePageSummary', { id: currentPage.id }),
      async () => {
        editPageOpen = false;
        await loadCurrentPage();
        await loadPages();
        if (parentChain.length > 0) {
          parentChain = parentChain.map((p, i) =>
            i === parentChain.length - 1 ? { ...p, label: form.localization.trim() } : p,
          );
        }
      },
      diffFields(
        currentPage as unknown as Record<string, unknown>,
        { ...form, name: form.name.trim() || null },
        PAGE_FIELDS(),
      ),
    );
  }

  function stageDeletePage() {
    if (!canManage || !currentPage) return;

    stage(
      'deletePage',
      translate('catalogAdmin.deletePage'),
      '/api/v1/operations/catalog/pages/delete',
      true,
      { pageId: currentPage.id },
      translate('catalogAdmin.deletePageSummary', { id: currentPage.id, name: currentPage.localization })
        + ' '
        + translate('catalogAdmin.deletePageBlockedNote'),
      async () => {
        goUp();
      },
    );
  }


  // "Localization id" is a free-form internal slug (see tools/catalog_converter/convert.py --
  // it's copied verbatim from the source catalog's own name column, no required format). Asking
  // an operator to invent one from nothing is the friction point; pre-filling a reasonable unique
  // default they can just accept -- or edit -- removes the "what do I even type here" blocker.
  function openNewOffer() {
    if (newOfferOpen) {
      newOfferOpen = false;
      return;
    }

    if (!newOffer.localizationId.trim() && currentPage) {
      newOffer = {
        ...newOffer,
        localizationId: `${currentPage.localization}_offer_${currentPage.offers.length + 1}`,
      };
    }

    newOfferOpen = true;
  }

  function stageCreateOffer() {
    if (!canManage || !currentPage) return;

    const currencyTypeId = newOffer.currencyTypeId === '' ? null : Number(newOffer.currencyTypeId);

    stage(
      'createOffer',
      translate('catalogAdmin.newOffer'),
      '/api/v1/operations/catalog/offers',
      Boolean(newOffer.localizationId.trim()) && nonNegative(newOffer.costCredits),
      {
        pageId: currentPage.id,
        localizationId: newOffer.localizationId.trim(),
        costCredits: Number(newOffer.costCredits) || 0,
        costCurrency: Number(newOffer.costCurrency) || 0,
        currencyTypeId,
        canGift: newOffer.canGift,
        canBundle: newOffer.canBundle,
        clubLevel: Number(newOffer.clubLevel) || 0,
        discountPercent: Number(newOffer.discountPercent) || 0,
        visible: newOffer.visible,
      },
      translate('catalogAdmin.createOfferSummary', { name: newOffer.localizationId.trim(), id: currentPage.id }),
      async () => {
        newOfferOpen = false;
        newOffer = emptyOfferForm();
        await loadCurrentPage();
      },
    );
  }

  // The row as it was loaded, kept so the confirm dialog can show what actually changes.
  let editOfferOriginal: CatalogOfferRow | null = null;
  let editProductOriginal: CatalogProductRow | null = null;

  function startEditOffer(offer: CatalogOfferRow) {
    editOfferId = offer.id;
    editOfferOriginal = offer;
    editOfferForm = {
      localizationId: offer.localizationId,
      costCredits: offer.costCredits,
      costCurrency: offer.costCurrency,
      currencyTypeId: offer.currencyTypeId ?? '',
      canGift: offer.canGift,
      canBundle: offer.canBundle,
      clubLevel: offer.clubLevel,
      discountPercent: offer.discountPercent,
      visible: offer.visible,
    };
  }

  function stageUpdateOffer() {
    if (!canManage || !editOfferForm || editOfferId === null) return;

    const form = editOfferForm;

    const currencyTypeId = editOfferForm.currencyTypeId === '' ? null : Number(editOfferForm.currencyTypeId);

    stage(
      'updateOffer',
      translate('catalogAdmin.edit'),
      '/api/v1/operations/catalog/offers/update',
      Boolean(editOfferForm.localizationId.trim()) && nonNegative(editOfferForm.costCredits),
      {
        offerId: editOfferId,
        localizationId: editOfferForm.localizationId.trim(),
        costCredits: Number(editOfferForm.costCredits) || 0,
        costCurrency: Number(editOfferForm.costCurrency) || 0,
        currencyTypeId,
        canGift: editOfferForm.canGift,
        canBundle: editOfferForm.canBundle,
        clubLevel: Number(editOfferForm.clubLevel) || 0,
        discountPercent: Number(editOfferForm.discountPercent) || 0,
        visible: editOfferForm.visible,
      },
      translate('catalogAdmin.updateOfferSummary', { id: editOfferId }),
      async () => {
        const id = editOfferId;
        editOfferId = null;
        await loadCurrentPage();
        if (selectedOfferId === id) {
          await loadOfferDetail(id!);
        }
      },
      diffFields(
        editOfferOriginal as unknown as Record<string, unknown>,
        { ...form, localizationId: form.localizationId.trim(), currencyTypeId },
        OFFER_FIELDS(),
      ),
    );
  }


  function stageDeleteOffer(offer: CatalogOfferRow) {
    if (!canManage) return;

    stage(
      'deleteOffer',
      translate('catalogAdmin.deleteOffer'),
      '/api/v1/operations/catalog/offers/delete',
      true,
      { offerId: offer.id },
      translate('catalogAdmin.deleteOfferSummary', { id: offer.id, name: offer.localizationId }),
      async () => {
        if (selectedOfferId === offer.id) {
          selectedOfferId = null;
          offerDetail = null;
        }
        await loadCurrentPage();
      },
    );
  }

  function pickProductFurniture(apply: (row: PickerRow) => void) {
    picker = { title: translate('common.selectFurniture'), onSelect: apply };
  }

  function stageCreateProduct() {
    if (!canManage || selectedOfferId === null) return;

    stage(
      'createProduct',
      translate('catalogAdmin.addItem'),
      '/api/v1/operations/catalog/products',
      true,
      {
        offerId: selectedOfferId,
        productType: Number(newProduct.productType),
        furnitureDefinitionId: newProduct.furnitureDefinitionId ? Number(newProduct.furnitureDefinitionId) : null,
        extraParam: newProduct.extraParam.trim() ? newProduct.extraParam.trim() : null,
        quantity: Number(newProduct.quantity) || 1,
        uniqueSize: Number(newProduct.uniqueSize) || 0,
        uniqueRemaining: Number(newProduct.uniqueRemaining) || 0,
      },
      translate('catalogAdmin.addProductSummary', { id: selectedOfferId }),
      async () => {
        newProductOpen = false;
        newProduct = emptyProductForm();
        await loadOfferDetail(selectedOfferId!);
      },
    );
  }

  function startEditProduct(product: CatalogProductRow) {
    editProductId = product.id;
    editProductOriginal = product;
    editProductForm = {
      productType: product.productType,
      furnitureDefinitionId: product.furnitureDefinitionEntityId ?? '',
      furnitureSprite: product.furnitureSpriteId ?? '',
      furnitureName: product.furnitureName || '',
      // The row carries the icon; the edit form was never given it, so the preview beside the
      // picker was blank until the operator re-picked.
      furnitureIcon: product.furnitureIconUrl || '',
      extraParam: product.extraParam || '',
      quantity: product.quantity,
      uniqueSize: product.uniqueSize,
      uniqueRemaining: product.uniqueRemaining,
    };
  }

  function stageUpdateProduct() {
    if (!canManage || !editProductForm || editProductId === null) return;

    const form = editProductForm;

    stage(
      'updateProduct',
      translate('catalogAdmin.edit'),
      '/api/v1/operations/catalog/products/update',
      true,
      {
        productId: editProductId,
        productType: Number(editProductForm.productType),
        furnitureDefinitionId: editProductForm.furnitureDefinitionId ? Number(editProductForm.furnitureDefinitionId) : null,
        extraParam: editProductForm.extraParam.trim() ? editProductForm.extraParam.trim() : null,
        quantity: Number(editProductForm.quantity) || 1,
        uniqueSize: Number(editProductForm.uniqueSize) || 0,
        uniqueRemaining: Number(editProductForm.uniqueRemaining) || 0,
      },
      translate('catalogAdmin.updateProductSummary', { id: editProductId }),
      async () => {
        editProductId = null;
        await loadOfferDetail(selectedOfferId!);
      },
      diffFields(
        // The loaded product names the definition `furnitureDefinitionEntityId`; the form uses the
        // shorter key the endpoint takes. Aligned here so the diff compares like with like.
        {
          ...editProductOriginal,
          furnitureDefinitionId: editProductOriginal?.furnitureDefinitionEntityId ?? '',
        },
        form,
        PRODUCT_FIELDS(),
      ),
    );
  }


  function stageDeleteProduct(product: CatalogProductRow) {
    if (!canManage) return;

    stage(
      'deleteProduct',
      translate('catalogAdmin.deleteProduct'),
      '/api/v1/operations/catalog/products/delete',
      true,
      { productId: product.id },
      translate('catalogAdmin.deleteProductSummary', { id: product.id }),
      async () => {
        await loadOfferDetail(selectedOfferId!);
      },
    );
  }

  onMount(async () => {
    await loadPages();
    try {
      const data = await apiGet<CatalogCurrencyList>('/api/v1/catalog/currency-types');
      currencyTypes = data.items || [];
    } catch {
      // Non-fatal: the offer forms fall back to a plain numeric currency id.
    }
    try {
      const data = await apiGet<CatalogIconTemplate>('/api/v1/catalog/icon-template');
      iconTemplate = data.template || '';
    } catch {
      // Non-fatal: falls back to showing just the numeric icon id with no preview image.
    }
  });
</script>

<section class="panel">
  <PageHeader title={$t('catalogAdmin.title')} description={$t('catalogAdmin.description')}>
    {#snippet actions()}
      <button type="button" class="warning" onclick={refreshAll} disabled={pagesLoading}>{$t('common.refresh')}</button>
    {/snippet}
  </PageHeader>
</section>

<section class="panel" style="margin-top: 12px;">
  <div class="catalog-tabs">
    {#each CATALOG_TYPES as ct}
      <button type="button" class="catalog-tab" class:active={catalogType === ct.value} onclick={() => switchCatalogType(ct.value)}>
        {$t(ct.key)}
      </button>
    {/each}
  </div>
</section>

<section class="panel" style="margin-top: 12px;">
  <nav class="breadcrumb" aria-label={$t('catalogAdmin.breadcrumbLabel')}>
    <button type="button" class="crumb-button" class:active={parentChain.length === 0} onclick={() => drillToBreadcrumb(-1)}>
      {$t('catalogAdmin.root')}
    </button>
    {#each parentChain as crumb, i}
      <ChevronRight size={14} strokeWidth={2} class="muted" aria-hidden="true" />
      <button type="button" class="crumb-button" class:active={i === parentChain.length - 1} onclick={() => drillToBreadcrumb(i)}>
        {crumb.label}
      </button>
    {/each}
  </nav>
</section>

{#if pagesForbidden}
  <AccessDeniedNotice message={$t('catalogAdmin.accessDenied')} />
{:else}
  {#if currentPage}
    <section class="panel">
      <div class="panel-head">
        <div class="page-heading">
          <AssetImage src={currentPage.iconUrl} alt="" size={40} fallbackIcon={FolderOpen} />
          <div>
            <h2>{currentPage.name || currentPage.localization}</h2>
            <small class="muted">{currentPage.localization} - #{currentPage.id}</small>
          </div>
        </div>
        {#if canManage}
          <div class="op-actions">
            <button type="button" class="ghost-button" onclick={startEditPage}>
              {$t('catalogAdmin.edit')}
            </button>
            <button type="button" class="ghost-button danger" onclick={stageDeletePage}>
              {$t('catalogAdmin.deletePage')}
            </button>
          </div>
        {/if}
      </div>
      <div class="metric-grid compact">
        <StatCard label={$t('catalogAdmin.layout')} value={currentPage.layout}>
          {#snippet icon()}
            <Activity size={15} strokeWidth={2} aria-hidden="true" />
          {/snippet}
        </StatCard>
        <StatCard label={$t('catalogAdmin.icon')}>
          {#snippet icon()}
            <Image size={15} strokeWidth={2} aria-hidden="true" />
          {/snippet}
          {#snippet value()}
                    <strong class="icon-preview">
              {#if currentPage?.iconUrl}<img src={currentPage?.iconUrl} alt="" loading="lazy" />{/if}
              #{currentPage?.icon}
            </strong>
          {/snippet}
        </StatCard>
        <StatCard label={$t('catalogAdmin.sortOrder')} value={currentPage.sortOrder}>
          {#snippet icon()}
            <Hash size={15} strokeWidth={2} aria-hidden="true" />
          {/snippet}
        </StatCard>
        <StatCard label={$t('catalogAdmin.visible')}>
          {#snippet icon()}
            <Activity size={15} strokeWidth={2} aria-hidden="true" />
          {/snippet}
          {#snippet value()}
                    <span>
              <span class="status-badge" class:status-badge--ok={currentPage?.visible} class:status-badge--bad={!currentPage?.visible}>
                {currentPage?.visible ? $t('catalogAdmin.visible') : $t('catalogAdmin.hidden')}
              </span>
            </span>
          {/snippet}
        </StatCard>
        <StatCard label={$t('catalogAdmin.imageData')} value={$t('catalogAdmin.lineCount', { count: (currentPage.imageData || []).length })}>
          {#snippet icon()}
            <Hash size={15} strokeWidth={2} aria-hidden="true" />
          {/snippet}
        </StatCard>
        <StatCard label={$t('catalogAdmin.textData')} value={$t('catalogAdmin.lineCount', { count: (currentPage.textData || []).length })}>
          {#snippet icon()}
            <Hash size={15} strokeWidth={2} aria-hidden="true" />
          {/snippet}
        </StatCard>
      </div>


      {#if canManage && ($ops.errors.deletePage || $ops.results.deletePage)}
        {#if $ops.errors.deletePage}<p class="empty-state danger" role="alert">{$ops.errors.deletePage}</p>{/if}
        {#if $ops.results.deletePage}
          <OpResult result={$ops.results.deletePage} />
        {/if}
      {/if}
    </section>
  {/if}

  <section class="panel">
    <div class="panel-head">
      <h2><Folder size={17} strokeWidth={2} aria-hidden="true" /> {$t('catalogAdmin.subPages')}</h2>
      {#if canManage}
        <button type="button" class="success" onclick={() => (newPageOpen = true)}>
          {$t('catalogAdmin.newPage')}
        </button>
      {/if}
    </div>


    {#if pagesLoading}
      <p class="muted">{$t('catalogAdmin.loadingPages')}</p>
    {:else if pagesError}
      <p class="empty-state danger" role="alert">{pagesError}</p>
    {:else if pages.length === 0}
      <p class="empty-state">{$t('catalogAdmin.noSubPages')}</p>
    {:else}
      <div class="catalog-list">
        {#each pages as page (page.id)}
          <button type="button" class="catalog-row" onclick={() => drillInto(page)}>
            <AssetImage src={page.iconUrl} alt="" size={38} fallbackIcon={Folder} />
            <span class="catalog-row-main">
              <strong>{page.name || page.localization}</strong>
              <small class="muted">{page.localization} - #{page.id}</small>
            </span>
            <span class="catalog-row-meta">
              <span class="op-chip" title={$t('catalogAdmin.subPagesTooltip')}>{page.childCount}</span>
              <span class="op-chip" title={$t('catalogAdmin.offersTooltip')}>{page.offerCount}</span>
              <span class="status-badge" class:status-badge--ok={page.visible} class:status-badge--bad={!page.visible}>
                {#if page.visible}{$t('catalogAdmin.visible')}{:else}{$t('catalogAdmin.hidden')}{/if}
              </span>
            </span>
            </button>
        {/each}
      </div>
    {/if}
  </section>

  {#if currentPage}
    <section class="panel">
      <div class="panel-head">
        <h2><Tag size={17} strokeWidth={2} aria-hidden="true" /> {$t('catalogAdmin.offersOnPage')}</h2>
        {#if canManage}
          <button type="button" class="success" onclick={openNewOffer}>
            {$t('catalogAdmin.newOffer')}
          </button>
        {/if}
      </div>


      {#if currentPage.offers.length === 0}
        <p class="empty-state">{$t('catalogAdmin.noOffers')}</p>
      {:else}
        <div class="catalog-list">
          {#each currentPage.offers as offer (offer.id)}
            <div class="catalog-card">
              <div class="catalog-row static">
                <span class="catalog-row-icon"><Tag size={18} strokeWidth={2} aria-hidden="true" /></span>
                <span class="catalog-row-main">
                  <strong>{offer.localizationId}</strong>
                  <!-- The id and the single product it delivers are one line about the same offer.
                       The product used to be a block of its own below the whole row, which read as
                       a second record rather than as this one's detail. -->
                  <span class="catalog-row-line">
                    {#if offer.productCount === 1 && offer.singleProduct}
                      <AssetImage
                        src={offer.singleProduct.furnitureIconUrl}
                        alt=""
                        size={22}
                        fallbackIcon={Image}
                      />
                    {/if}
                    <span class="catalog-row-id muted">#{offer.id}{offer.clubLevel > 0 ? ` - HC${offer.clubLevel}` : ''}{offer.discountPercent > 0 ? ` - -${offer.discountPercent}%` : ''}</span>
                    {#if offer.productCount === 1 && offer.singleProduct}
                      <span class="catalog-row-product">{offer.singleProduct.furnitureName || offer.singleProduct.productTypeLabel}</span>
                      <span class="op-chip" title={$t('common.quantityLabel')}>x{offer.singleProduct.quantity}</span>
                      {#if offer.singleProduct.uniqueSize > 0}
                        <span class="op-chip" title={$t('common.uniqueRemaining')}>{offer.singleProduct.uniqueRemaining}/{offer.singleProduct.uniqueSize}</span>
                      {/if}
                    {/if}
                  </span>
                </span>
                <span class="catalog-row-meta">
                  <!-- currencyTypeId 0 is duckets, so it is tested against null, not for truth. -->
                  {#if offer.costCredits > 0 || offer.currencyTypeId == null}
                    <span class={currencyChipClass(CURRENCY_KIND.credits)}>
                      <CurrencyIcon kind={CURRENCY_KIND.credits} size={13} />
                      {formatNumber(offer.costCredits)}
                    </span>
                  {/if}
                  {#if offer.currencyTypeId != null}
                    <span class={currencyChipClass(currencyKindFromRewardType(offer.currencyTypeId))}>
                      <CurrencyIcon kind={currencyKindFromRewardType(offer.currencyTypeId)} size={13} />
                      {formatNumber(offer.costCurrency)}
                      {offer.currencyName || `#${offer.currencyTypeId}`}
                    </span>
                  {/if}
                  <span class="status-badge" class:status-badge--ok={offer.visible} class:status-badge--bad={!offer.visible}>
                    {#if offer.visible}{$t('catalogAdmin.visible')}{:else}{$t('catalogAdmin.hidden')}{/if}
                  </span>
                </span>
                <div class="op-actions">
                  <button type="button" class="ghost-button" class:active={selectedOfferId === offer.id} onclick={() => toggleOfferDetail(offer.id)}>
                    {offerActionLabel(offer, selectedOfferId, $t)}
                  </button>
                  {#if canManage}
                    <button type="button" class="ghost-button" onclick={() => startEditOffer(offer)}>
                      {$t('catalogAdmin.edit')}
                    </button>
                    <!-- "Delete offer" beside Manage and Edit was twice their length for no extra
                         meaning: the row is an offer. The confirmation still names it in full. -->
                    <button type="button" class="ghost-button danger" onclick={() => stageDeleteOffer(offer)}>
                      {$t('common.delete')}
                    </button>
                  {/if}
                </div>
              </div>





              {#if selectedOfferId === offer.id}
                <div class="catalog-card-detail products-panel">
                  <div class="panel-head">
                    <h3><Package size={15} strokeWidth={2} aria-hidden="true" /> {$t('catalogAdmin.itemsDelivered')}</h3>
                    {#if canManage}
                      <button type="button" class="success" onclick={() => (newProductOpen = true)}>
                        {$t('catalogAdmin.addItem')}
                      </button>
                    {/if}
                  </div>

                  {#if offerDetailLoading}
                    <p class="muted">{$t('catalogAdmin.loadingProducts')}</p>
                  {:else if offerDetailError}
                    <p class="empty-state danger" role="alert">{offerDetailError}</p>
                  {:else if offerDetail}

                    {#if offerDetail.products.length === 0}
                      <p class="empty-state">{$t('catalogAdmin.noProducts')}</p>
                    {:else}
                      <div class="catalog-list">
                        {#each offerDetail.products as product (product.id)}
                          <div class="catalog-card">
                            <div class="catalog-row static">
                              <AssetImage
                                src={product.furnitureIconUrl}
                                alt=""
                                size={38}
                                fallbackIcon={Image}
                              />
                              <span class="catalog-row-main">
                                <strong>{product.furnitureName || product.productTypeLabel}</strong>
                                <small class="muted">{product.productTypeLabel}{product.furnitureDefinitionEntityId ? ` - #${product.furnitureDefinitionEntityId}` : ''}</small>
                              </span>
                              <span class="catalog-row-meta">
                                <span class="op-chip" title={$t('common.quantityLabel')}>x{product.quantity}</span>
                                {#if product.uniqueSize > 0}
                                  <span class="op-chip" title={$t('common.uniqueRemaining')}>{product.uniqueRemaining}/{product.uniqueSize}</span>
                                {/if}
                              </span>
                              {#if canManage}
                                <button type="button" class="ghost-button" onclick={() => startEditProduct(product)}>
                                  {$t('catalogAdmin.edit')}
                                </button>
                                <button type="button" class="ghost-button danger" onclick={() => stageDeleteProduct(product)}>
                                  {$t('catalogAdmin.deleteProduct')}
                                </button>
                              {/if}
                            </div>


                          </div>
                        {/each}
                      </div>
                    {/if}
                    {#if $ops.errors.deleteProduct}<p class="empty-state danger" role="alert">{$ops.errors.deleteProduct}</p>{/if}
                    {#if $ops.results.deleteProduct}
                      <OpResult result={$ops.results.deleteProduct} />
                    {/if}
                  {/if}
                </div>
              {/if}
            </div>
          {/each}
        </div>
      {/if}
      {#if $ops.errors.deleteOffer}<p class="empty-state danger" role="alert">{$ops.errors.deleteOffer}</p>{/if}
      {#if $ops.results.deleteOffer}
        <OpResult result={$ops.results.deleteOffer} />
      {/if}
    </section>
  {/if}
{/if}

{#if picker}
  <PickerModal
    kind="furniture"
    title={picker.title}
    onSelect={picker.onSelect}
    onClose={() => (picker = null)}
    canSelect={canManage}
  />
{/if}

{#if iconPickerTarget}
  <CatalogIconPickerModal
    title={$t('catalogAdmin.selectCatalogIcon')}
    onSelect={(id) => {
      if (iconPickerTarget === 'new') {
        newPage = { ...newPage, icon: id };
      } else if (iconPickerTarget === 'edit' && editPageForm) {
        editPageForm = { ...editPageForm, icon: id };
      }
    }}
    onClose={() => (iconPickerTarget = null)}
  />
{/if}

{#if editPageOpen && editPageForm}
  <Drawer title={$t('catalogAdmin.editPage')} eyebrow={$t('catalogAdmin.title')} onclose={() => { editPageOpen = false; }}>
    <div class="catalog-card-detail">
      <div class="op-field">
        <label for="edit-page-localization">{$t('catalogAdmin.localizationKeyRequired')}</label>
        <input autocomplete="off" spellcheck="false" id="edit-page-localization" bind:value={editPageForm.localization} />
      </div>
      <div class="op-field">
        <label for="edit-page-name">{$t('catalogAdmin.displayName')}</label>
        <input autocomplete="off" spellcheck="false" id="edit-page-name" bind:value={editPageForm.name} />
      </div>
      <div class="op-field">
        <span class="op-label">{$t('catalogAdmin.icon')}</span>
        <div class="op-pick">
          <button class="ghost-button" type="button" onclick={() => (iconPickerTarget = 'edit')}>
            {$t('catalogAdmin.selectIcon')}
          </button>
          <span class="op-chip">
            {#if iconUrlFor(editPageForm.icon)}<img class="op-sprite" src={iconUrlFor(editPageForm.icon)} alt="" loading="lazy" />{:else}<span class="op-sprite">{editPageForm.icon}</span>{/if}
            #{editPageForm.icon}
          </span>
        </div>
      </div>
      <div class="op-field">
        <label for="edit-page-layout">{$t('catalogAdmin.layout')}</label>
        <select id="edit-page-layout" bind:value={editPageForm.layout}>
          {#each LAYOUT_OPTIONS as l}<option value={l}>{formatLayoutLabel(l)}</option>{/each}
        </select>
      </div>
      <div class="op-field">
        <label for="edit-page-sort">{$t('catalogAdmin.sortOrder')}</label>
        <input autocomplete="off" spellcheck="false" id="edit-page-sort" type="number" bind:value={editPageForm.sortOrder} />
      </div>
      <div class="op-field">
        <label><input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={editPageForm.visible} /> {$t('catalogAdmin.visible')}</label>
      </div>
      <div class="op-field">
        <label for="edit-page-image-data">{$t('catalogAdmin.imageDataOptional')}</label>
        <textarea id="edit-page-image-data" rows="3" bind:value={editPageForm.imageDataText} placeholder="promo_banner.png"></textarea>
      </div>
      <div class="op-field">
        <label for="edit-page-text-data">{$t('catalogAdmin.textDataOptional')}</label>
        <textarea id="edit-page-text-data" rows="3" bind:value={editPageForm.textDataText} placeholder={$t('catalogAdmin.textDataPlaceholder')}></textarea>
      </div>
      {#if $ops.errors.updatePage}<p class="empty-state danger" role="alert">{$ops.errors.updatePage}</p>{/if}
      {#if $ops.results.updatePage}
        <OpResult result={$ops.results.updatePage} />
      {/if}
    </div>
  
    {#snippet actions()}
      <button type="button" onclick={stageUpdatePage} disabled={$ops.busyKeys.updatePage}>{$t('catalogAdmin.save')}</button>
      <button class="ghost-button" type="button" onclick={() => (editPageOpen = false)}>{$t('catalogAdmin.cancel')}</button>
    {/snippet}
  </Drawer>
{/if}

{#if newPageOpen}
  <Drawer title={$t('catalogAdmin.newPage')} eyebrow={$t('catalogAdmin.title')} onclose={() => { newPageOpen = false; }}>
    <div class="catalog-card-detail">
      <div class="op-field">
        <label for="new-page-localization">{$t('catalogAdmin.localizationKeyRequired')}</label>
        <input autocomplete="off" spellcheck="false" id="new-page-localization" bind:value={newPage.localization} placeholder={$t('catalogAdmin.localizationPlaceholder')} />
      </div>
      <div class="op-field">
        <label for="new-page-name">{$t('catalogAdmin.displayName')}</label>
        <input autocomplete="off" spellcheck="false" id="new-page-name" bind:value={newPage.name} />
      </div>
      <div class="op-field">
        <span class="op-label">{$t('catalogAdmin.icon')}</span>
        <div class="op-pick">
          <button class="ghost-button" type="button" onclick={() => (iconPickerTarget = 'new')}>
            {$t('catalogAdmin.selectIcon')}
          </button>
          <span class="op-chip">
            {#if iconUrlFor(newPage.icon)}<img class="op-sprite" src={iconUrlFor(newPage.icon)} alt="" loading="lazy" />{:else}<span class="op-sprite">{newPage.icon}</span>{/if}
            #{newPage.icon}
          </span>
        </div>
      </div>
      <div class="op-field">
        <label for="new-page-layout">{$t('catalogAdmin.layout')}</label>
        <select id="new-page-layout" bind:value={newPage.layout}>
          {#each LAYOUT_OPTIONS as l}<option value={l}>{formatLayoutLabel(l)}</option>{/each}
        </select>
      </div>
      <div class="op-field">
        <label for="new-page-sort">{$t('catalogAdmin.sortOrder')}</label>
        <input autocomplete="off" spellcheck="false" id="new-page-sort" type="number" bind:value={newPage.sortOrder} />
      </div>
      <div class="op-field">
        <label><input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={newPage.visible} /> {$t('catalogAdmin.visible')}</label>
      </div>
      <div class="op-field">
        <label for="new-page-image-data">{$t('catalogAdmin.imageDataOptional')}</label>
        <textarea id="new-page-image-data" rows="3" bind:value={newPage.imageDataText} placeholder="promo_banner.png"></textarea>
      </div>
      <div class="op-field">
        <label for="new-page-text-data">{$t('catalogAdmin.textDataOptional')}</label>
        <textarea id="new-page-text-data" rows="3" bind:value={newPage.textDataText} placeholder={$t('catalogAdmin.textDataPlaceholder')}></textarea>
      </div>
      {#if $ops.errors.createPage}<p class="empty-state danger" role="alert">{$ops.errors.createPage}</p>{/if}
      {#if $ops.results.createPage}
        <OpResult result={$ops.results.createPage} />
      {/if}
    </div>
  
    {#snippet actions()}
      <button type="button" onclick={stageCreatePage} disabled={$ops.busyKeys.createPage} class="success">{$t('catalogAdmin.create')}</button>
    {/snippet}
  </Drawer>
{/if}

{#if newOfferOpen}
  <Drawer title={$t('catalogAdmin.newOffer')} eyebrow={$t('catalogAdmin.title')} onclose={() => { newOfferOpen = false; }}>
    <div class="catalog-card-detail">
      <div class="op-field">
        <label for="new-offer-localization">{$t('catalogAdmin.localizationIdRequired')}</label>
        <input autocomplete="off" spellcheck="false" id="new-offer-localization" bind:value={newOffer.localizationId} />
        <small class="muted">{$t('catalogAdmin.localizationIdHint')}</small>
      </div>
      <div class="op-field">
        <label for="new-offer-credits">{$t('catalogAdmin.costCredits')}</label>
        <input autocomplete="off" spellcheck="false" id="new-offer-credits" type="number" min="0" bind:value={newOffer.costCredits} />
      </div>
      <div class="op-field">
        <label for="new-offer-currency-amount">{$t('catalogAdmin.costSecondary')}</label>
        <input autocomplete="off" spellcheck="false" id="new-offer-currency-amount" type="number" min="0" bind:value={newOffer.costCurrency} />
      </div>
      <div class="op-field">
        <label for="new-offer-currency-type">{$t('catalogAdmin.currencyTypeOptional')}</label>
        <select id="new-offer-currency-type" bind:value={newOffer.currencyTypeId}>
          <option value="">{$t('catalogAdmin.none')}</option>
          {#each activityPointCurrencies as c}<option value={c.activityPointType}>{c.name || c.type} (#{c.activityPointType})</option>{/each}
        </select>
      </div>
      <div class="op-field">
        <label for="new-offer-club-level">{$t('catalogAdmin.clubLevel')}</label>
        <input autocomplete="off" spellcheck="false" id="new-offer-club-level" type="number" min="0" bind:value={newOffer.clubLevel} />
      </div>
      <div class="op-field">
        <label for="new-offer-discount">{$t('catalogAdmin.discountPercent')}</label>
        <input autocomplete="off" spellcheck="false" id="new-offer-discount" type="number" min="0" max="100" bind:value={newOffer.discountPercent} />
      </div>
      <div class="op-field">
        <label><input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={newOffer.canGift} /> {$t('catalogAdmin.canGift')}</label>
      </div>
      <div class="op-field">
        <label><input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={newOffer.canBundle} /> {$t('catalogAdmin.canBundle')}</label>
      </div>
      <div class="op-field">
        <label><input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={newOffer.visible} /> {$t('catalogAdmin.visible')}</label>
      </div>
      {#if $ops.errors.createOffer}<p class="empty-state danger" role="alert">{$ops.errors.createOffer}</p>{/if}
      {#if $ops.results.createOffer}
        <OpResult result={$ops.results.createOffer} />
      {/if}
    </div>
  
    {#snippet actions()}
      <button type="button" onclick={stageCreateOffer} disabled={$ops.busyKeys.createOffer} class="success">{$t('catalogAdmin.create')}</button>
    {/snippet}
  </Drawer>
{/if}

{#if editOfferId !== null && editOfferForm}
  <Drawer title={$t('catalogAdmin.editOffer')} eyebrow={$t('catalogAdmin.title')} onclose={() => { editOfferId = null; editOfferForm = null; }}>
    <div class="catalog-card-detail">
      <div class="op-field">
        <label for={`edit-offer-localization-${editOfferId}`}>{$t('catalogAdmin.localizationIdRequired')}</label>
        <input autocomplete="off" spellcheck="false" id={`edit-offer-localization-${editOfferId}`} bind:value={editOfferForm.localizationId} />
      </div>
      <div class="op-field">
        <label for={`edit-offer-credits-${editOfferId}`}>{$t('catalogAdmin.costCredits')}</label>
        <input autocomplete="off" spellcheck="false" id={`edit-offer-credits-${editOfferId}`} type="number" min="0" bind:value={editOfferForm.costCredits} />
      </div>
      <div class="op-field">
        <label for={`edit-offer-currency-amount-${editOfferId}`}>{$t('catalogAdmin.costSecondary')}</label>
        <input autocomplete="off" spellcheck="false" id={`edit-offer-currency-amount-${editOfferId}`} type="number" min="0" bind:value={editOfferForm.costCurrency} />
      </div>
      <div class="op-field">
        <label for={`edit-offer-currency-type-${editOfferId}`}>{$t('catalogAdmin.currencyTypeOptional')}</label>
        <select id={`edit-offer-currency-type-${editOfferId}`} bind:value={editOfferForm.currencyTypeId}>
          <option value="">{$t('catalogAdmin.none')}</option>
          {#each activityPointCurrencies as c}<option value={c.activityPointType}>{c.name || c.type} (#{c.activityPointType})</option>{/each}
        </select>
      </div>
      <div class="op-field">
        <label for={`edit-offer-club-${editOfferId}`}>{$t('catalogAdmin.clubLevel')}</label>
        <input autocomplete="off" spellcheck="false" id={`edit-offer-club-${editOfferId}`} type="number" min="0" bind:value={editOfferForm.clubLevel} />
      </div>
      <div class="op-field">
        <label for={`edit-offer-discount-${editOfferId}`}>{$t('catalogAdmin.discountPercent')}</label>
        <input autocomplete="off" spellcheck="false" id={`edit-offer-discount-${editOfferId}`} type="number" min="0" max="100" bind:value={editOfferForm.discountPercent} />
      </div>
      <div class="op-field"><label><input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={editOfferForm.canGift} /> {$t('catalogAdmin.canGift')}</label></div>
      <div class="op-field"><label><input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={editOfferForm.canBundle} /> {$t('catalogAdmin.canBundle')}</label></div>
      <div class="op-field"><label><input autocomplete="off" spellcheck="false" type="checkbox" bind:checked={editOfferForm.visible} /> {$t('catalogAdmin.visible')}</label></div>
      {#if $ops.errors.updateOffer}<p class="empty-state danger" role="alert">{$ops.errors.updateOffer}</p>{/if}
      {#if $ops.results.updateOffer}
        <OpResult result={$ops.results.updateOffer} />
      {/if}
    </div>
  
    {#snippet actions()}
      <button type="button" onclick={stageUpdateOffer} disabled={$ops.busyKeys.updateOffer}>{$t('catalogAdmin.save')}</button>
      <button class="ghost-button" type="button" onclick={() => (editOfferId = null)}>{$t('catalogAdmin.cancel')}</button>
    {/snippet}
  </Drawer>
{/if}

{#if newProductOpen}
  <Drawer title={$t('catalogAdmin.newProduct')} eyebrow={$t('catalogAdmin.title')} onclose={() => { newProductOpen = false; }}>
    <div class="catalog-card-detail">
      <div class="op-field">
        <label for="new-product-type">{$t('catalogAdmin.productType')}</label>
        <select id="new-product-type" bind:value={newProduct.productType}>
          {#each PRODUCT_TYPES as pt}<option value={pt.value}>{pt.label}</option>{/each}
        </select>
      </div>
      <div class="op-field">
        <span class="op-label">{$t('catalogAdmin.furnitureOptional')}</span>
        <div class="op-pick">
          <button class="success"
            type="button"
            onclick={() => pickProductFurniture((f) => (newProduct = {
                ...newProduct,
                furnitureDefinitionId: Number(f.id),
                furnitureName: f.name,
                furnitureSprite: f.spriteId ?? '',
                furnitureIcon: f.iconUrl ?? '',
              }))}
          >
            {$t('common.selectFurniture')}
          </button>
          {#if newProduct.furnitureDefinitionId}
            <span class="op-chip">
              {#if newProduct.furnitureIcon}<img class="op-sprite" src={newProduct.furnitureIcon} alt="" loading="lazy" />{:else}<span class="op-sprite">{newProduct.furnitureSprite}</span>{/if}
              {newProduct.furnitureName} <small>#{newProduct.furnitureDefinitionId}</small>
            </span>
            <button class="ghost-button" type="button" onclick={() => (newProduct = { ...newProduct, furnitureDefinitionId: '', furnitureName: '', furnitureIcon: '', furnitureSprite: '' })}>{$t('catalogAdmin.clear')}</button>
          {:else}
            <span class="muted">{$t('common.noFurnitureSelected')}</span>
          {/if}
        </div>
      </div>
      <div class="op-field">
        <label for="new-product-extra">{$t('catalogAdmin.extraData')}</label>
        <input autocomplete="off" spellcheck="false" id="new-product-extra" bind:value={newProduct.extraParam} placeholder={$t('operations.extraDataPlaceholder')} />
      </div>
      <div class="op-field">
        <label for="new-product-quantity">{$t('catalogAdmin.quantity')}</label>
        <input autocomplete="off" spellcheck="false" id="new-product-quantity" type="number" min="1" bind:value={newProduct.quantity} />
      </div>
      <div class="op-field">
        <label for="new-product-unique-size">{$t('catalogAdmin.uniqueSizeHint')}</label>
        <input autocomplete="off" spellcheck="false" id="new-product-unique-size" type="number" min="0" bind:value={newProduct.uniqueSize} />
      </div>
      <div class="op-field">
        <label for="new-product-unique-remaining">{$t('catalogAdmin.uniqueRemaining')}</label>
        <input autocomplete="off" spellcheck="false" id="new-product-unique-remaining" type="number" min="0" bind:value={newProduct.uniqueRemaining} />
      </div>
      {#if $ops.errors.createProduct}<p class="empty-state danger" role="alert">{$ops.errors.createProduct}</p>{/if}
      {#if $ops.results.createProduct}
        <OpResult result={$ops.results.createProduct} />
      {/if}
    </div>
  
    {#snippet actions()}
      <button type="button" onclick={stageCreateProduct} disabled={$ops.busyKeys.createProduct} class="success">{$t('catalogAdmin.create')}</button>
    {/snippet}
  </Drawer>
{/if}

{#if editProductId !== null && editProductForm}
  <Drawer title={$t('catalogAdmin.editProduct')} eyebrow={$t('catalogAdmin.title')} onclose={() => { editProductId = null; editProductForm = null; }}>
    <div class="catalog-card-detail">
      <div class="op-field">
        <label for={`edit-product-type-${editProductId}`}>{$t('catalogAdmin.productType')}</label>
        <select id={`edit-product-type-${editProductId}`} bind:value={editProductForm.productType}>
          {#each PRODUCT_TYPES as pt}<option value={pt.value}>{pt.label}</option>{/each}
        </select>
      </div>
      <div class="op-field">
        <label for={`edit-product-def-${editProductId}`}>{$t('catalogAdmin.furnitureDefIdOptional')}</label>
        <input autocomplete="off" spellcheck="false" id={`edit-product-def-${editProductId}`} type="number" min="0" bind:value={editProductForm.furnitureDefinitionId} />
      </div>
      <div class="op-field">
        <label for={`edit-product-extra-${editProductId}`}>{$t('catalogAdmin.extraData')}</label>
        <input autocomplete="off" spellcheck="false" id={`edit-product-extra-${editProductId}`} bind:value={editProductForm.extraParam} />
      </div>
      <div class="op-field">
        <label for={`edit-product-qty-${editProductId}`}>{$t('catalogAdmin.quantity')}</label>
        <input autocomplete="off" spellcheck="false" id={`edit-product-qty-${editProductId}`} type="number" min="1" bind:value={editProductForm.quantity} />
      </div>
      <div class="op-field">
        <label for={`edit-product-usize-${editProductId}`}>{$t('catalogAdmin.uniqueSizeHint')}</label>
        <input autocomplete="off" spellcheck="false" id={`edit-product-usize-${editProductId}`} type="number" min="0" bind:value={editProductForm.uniqueSize} />
      </div>
      <div class="op-field">
        <label for={`edit-product-urem-${editProductId}`}>{$t('catalogAdmin.uniqueRemaining')}</label>
        <input autocomplete="off" spellcheck="false" id={`edit-product-urem-${editProductId}`} type="number" min="0" bind:value={editProductForm.uniqueRemaining} />
      </div>
      {#if $ops.errors.updateProduct}<p class="empty-state danger" role="alert">{$ops.errors.updateProduct}</p>{/if}
      {#if $ops.results.updateProduct}
        <OpResult result={$ops.results.updateProduct} />
      {/if}
    </div>
  
    {#snippet actions()}
      <button type="button" onclick={stageUpdateProduct} disabled={$ops.busyKeys.updateProduct}>{$t('catalogAdmin.save')}</button>
      <button class="ghost-button" type="button" onclick={() => (editProductId = null)}>{$t('catalogAdmin.cancel')}</button>
    {/snippet}
  </Drawer>
{/if}

<ConfirmReasonModal
  open={Boolean($ops.pending)}
  title={$ops.pending?.title ?? ''}
  changes={$ops.pending?.changes ?? []}
  noteOnly={$ops.pending?.noteOnly ?? false}
  summary={$ops.pending?.summary ?? ''}
  confirmLabel={$ops.pending?.title ?? $t('common.confirm')}
  busy={$ops.busy}
  error={$ops.error}
  danger={$ops.pending?.danger ?? false}
  onconfirm={ops.confirm}
  oncancel={() => ops.cancel()}
/>

<style>
  .catalog-tabs {
    display: flex;
    gap: 6px;
    margin-top: 10px;
    overflow-x: auto;
    flex-wrap: nowrap;
    padding-bottom: 8px;
    scrollbar-width: thin;
    scrollbar-color: var(--line-strong) transparent;
  }

  .catalog-tabs::-webkit-scrollbar {
    height: 6px;
  }

  .catalog-tabs::-webkit-scrollbar-track {
    background: transparent;
  }

  .catalog-tabs::-webkit-scrollbar-thumb {
    background: var(--line-strong);
    border-radius: 999px;
  }

  .catalog-tabs::-webkit-scrollbar-thumb:hover {
    background: var(--muted);
  }

  .catalog-tab {
    flex: 0 0 auto;
    padding: 7px 14px;
    border-radius: 8px;
    border: 1px solid var(--line);
    background: transparent;
    color: var(--muted);
    cursor: pointer;
    font-size: 0.85rem;
    white-space: nowrap;
  }

  .catalog-tab.active {
    border-color: var(--accent);
    color: var(--ink);
    background: rgba(var(--accent-rgb), 0.12);
  }

  .breadcrumb {
    display: flex;
    align-items: center;
    gap: 6px;
    margin-top: 10px;
    flex-wrap: nowrap;
    overflow-x: auto;
    padding-bottom: 8px;
    scrollbar-width: thin;
    scrollbar-color: var(--line-strong) transparent;
  }

  .breadcrumb::-webkit-scrollbar {
    height: 6px;
  }

  .breadcrumb::-webkit-scrollbar-track {
    background: transparent;
  }

  .breadcrumb::-webkit-scrollbar-thumb {
    background: var(--line-strong);
    border-radius: 999px;
  }

  .breadcrumb::-webkit-scrollbar-thumb:hover {
    background: var(--muted);
  }

  .breadcrumb button {
    flex: 0 0 auto;
    white-space: nowrap;
  }

  .crumb-button {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    padding: 6px 12px;
    border-radius: 999px;
    border: 1px solid var(--line);
    background: transparent;
    color: var(--muted);
    cursor: pointer;
    font-size: 0.82rem;
  }

  .crumb-button.active {
    border-color: var(--accent);
    color: var(--ink);
    background: rgba(var(--accent-rgb), 0.12);
  }

  .ghost-button.active {
    border-color: var(--accent);
    color: var(--ink);
    background: rgba(var(--accent-rgb), 0.12);
  }

  .ghost-button,
  .crumb-button,
  .catalog-tab,
  .panel-head h3 {
    display: flex;
    align-items: center;
    gap: 8px;
    margin: 0;
    font-size: 0.95rem;
  }

  .products-panel > .panel-head {
    margin-bottom: 10px;
  }

  .page-heading {
    display: flex;
    align-items: center;
    gap: 12px;
    min-width: 0;
    flex-wrap: wrap;
  }

  .page-heading h2 {
    margin: 0;
  }



  .catalog-row {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    row-gap: 8px;
    gap: 12px;
    width: 100%;
    padding: 10px 12px;
    background: transparent;
    border: none;
    color: inherit;
    text-align: left;
    cursor: pointer;
    font: inherit;
  }

  button.catalog-row:hover {
    background: var(--surface-hover);
  }

  .catalog-row.static {
    cursor: default;
  }

  .catalog-row-icon {
    width: 38px;
    height: 38px;
    flex: 0 0 auto;
    display: grid;
    place-items: center;
    border: 1px solid var(--line-strong);
    border-radius: 8px;
    background: var(--input-bg);
    color: var(--accent);
    overflow: hidden;
  }



  /* The offer's second line: its id, and the single product it delivers when there is only one --
     see offerActionLabel()'s doc comment for why that is shown without a click. */
  .catalog-row-line {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 8px;
    font-size: 0.82rem;
  }

  /* Every box on the line is the same height and the text is pinned to it, so nothing rides high
     or low: a flex row centres line-boxes, and a <small> next to a 22px sprite has a line-box of
     its own height, which is what made the two look off by a pixel or two. */
  .catalog-row-line > * {
    height: 22px;
    line-height: 22px;
    box-sizing: border-box;
  }

  .catalog-row-line :global(.asset-image) {
    border-radius: 5px;
  }

  /* The product is what the offer delivers -- the one thing on this line worth reading. It was
     `muted` like the id beside it, which is the colour for what you skip over. */
  .catalog-row-product {
    color: var(--ink);
    font-size: 0.86rem;
  }

  /* Whole pixels, deliberately. A <small> here inherited 0.82rem and took its own 0.8em of that,
     landing on ~10.5px -- a fractional size renders soft, which is what read as blur. */
  .catalog-row-id {
    font-size: 12px;
    font-variant-numeric: tabular-nums;
  }

  .catalog-row-meta {
    display: flex;
    align-items: center;
    gap: 6px;
    flex: 0 1 auto;
    flex-wrap: wrap;
  }

  /* .op-chip, .status-badge and .cost-chip are three differently-authored pill styles (two
     global, one local) with different padding/font-size, so side by side their heights don't
     match. Force them to the same box height here rather than touching the global classes,
     which are reused unrelated places elsewhere in the dashboard. */
  .catalog-row-meta > .op-chip,
  .catalog-row-meta > .status-badge,
  .catalog-row-meta > .cost-chip {
    height: 24px;
    box-sizing: border-box;
  }

  /* The chips state facts and the buttons do things; sharing the row's gap ran the two groups
     together into one undifferentiated line of boxes. */
  .catalog-row-meta + :global(.op-actions) {
    margin-left: 10px;
    padding-left: 14px;
    border-left: 1px solid var(--line);
  }

  .icon-preview {
    display: inline-flex;
    align-items: center;
    gap: 6px;
  }

  .icon-preview img {
    width: 20px;
    height: 20px;
    object-fit: contain;
    image-rendering: pixelated;
    image-rendering: crisp-edges;
  }
</style>
